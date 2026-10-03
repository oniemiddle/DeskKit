// Compiled only into the Windows target framework.
#if WINDOWS
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using DeskKit.Core.Abstractions;
using DeskKit.Platform.Interop;
using Microsoft.Extensions.Logging;

namespace DeskKit.Platform.Windows;

/// <summary>
/// Windows implementation of <see cref="IDesktopGestureService"/>: a low-level
/// mouse hook that recognises a double-click on an empty part of the desktop.
/// </summary>
/// <remarks>
/// Two constraints shape the whole class.
/// <para>
/// The hook callback runs on the input path of the whole machine, and Windows
/// serialises it: every other click waits for it, and one that takes too long is
/// answered by removing the hook. So the callback does the least possible — it
/// copies the press into a queue — and the hit test, which has to talk to
/// Explorer, is done later on a worker.
/// </para>
/// <para>
/// The hook is installed on a thread of its own with a message loop, rather than
/// on the UI thread: a low-level hook is delivered through the message queue of
/// the thread that installed it, and a UI thread busy with a layout pass or a
/// modal dialog is a thread whose messages are not being taken. The watcher
/// therefore stays alive while the application is doing something else.
/// </para>
/// </remarks>
public sealed class WindowsDesktopGestureService : IDesktopGestureService, IDisposable
{
    /// <summary>How long the hook thread is given to register before the attempt is abandoned.</summary>
    private const int StartupTimeoutMs = 1500;

    /// <summary>How long the hook thread is given to shut down before it is unhooked from outside.</summary>
    private const int StopTimeoutMs = 500;

    /// <summary>
    /// How many presses may wait for the worker. The queue exists to keep the
    /// callback short; a burst that outruns the worker is dropped, because a
    /// gesture is never worth delaying the machine's input for.
    /// </summary>
    private const int MaximumQueuedPresses = 16;

    /// <summary>
    /// The watcher the hook callback belongs to. The OS calls a plain native
    /// function pointer, which cannot carry an instance, and there is one watcher
    /// per process because there is one such hook per thread.
    /// </summary>
    private static WindowsDesktopGestureService? _watcher;

    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly ConcurrentQueue<QueuedPress> _presses = new();

    private volatile DesktopDoubleClickDetector? _detector;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private long _generation;
    private int _queuedPresses;
    private int _drainScheduled;
    private bool _disposed;

    public WindowsDesktopGestureService(ILogger<WindowsDesktopGestureService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <summary>
    /// True while the hook is registered. A machine where the hook was refused
    /// reads as unsupported, which is how the caller learns the gesture is not
    /// available without having to know why.
    /// </summary>
    public bool IsSupported
    {
        get
        {
            lock (_sync)
                return _hook != IntPtr.Zero;
        }
    }

    public event EventHandler? BackdropDoubleClicked;

    public void Start()
    {
        TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread;
        long generation;

        lock (_sync)
        {
            if (_disposed || _hook != IntPtr.Zero)
                return;

            // Read here rather than in the callback: a hook proc should not be asking
            // the system anything.
            _detector = new DesktopDoubleClickDetector(
                NativeMethods.GetDoubleClickTime(),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK),
                NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK));

            generation = ++_generation;
            Volatile.Write(ref _watcher, this);

            thread = new Thread(() => HookThreadMain(ready, generation))
            {
                IsBackground = true,
                Name = "DeskKit.DesktopGestureHook",
            };

            _thread = thread;
        }

        thread.Start();

        if (ready.Task.Wait(StartupTimeoutMs) && IsSupported)
            return;

        // A gesture that could not be watched is a missing feature, not a failure to
        // start: the caller is told, and the rest of the application is unaffected.
        _logger.LogWarning("The desktop double-click gesture was refused by the system");
        Stop();
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;
        IntPtr hook;

        lock (_sync)
        {
            // A new generation first: whatever the old thread has queued is now stale,
            // and a hook callback that arrives before it is unregistered must not feed
            // the new state.
            _generation++;
            thread = _thread;
            threadId = _threadId;
            hook = _hook;

            _thread = null;
            _threadId = 0;
            _hook = IntPtr.Zero;
            _detector = null;
            Volatile.Write(ref _watcher, null);
        }

        if (thread is not null)
        {
            // WM_QUIT ends the thread's message loop, which unhooks as it unwinds.
            if (threadId != 0)
                NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            if (thread != Thread.CurrentThread && !thread.Join(StopTimeoutMs))
            {
                // The thread did not come back. Unhooking from here is safe even if the
                // thread is already gone, and asking again after it gives up bounds the
                // wait rather than leaving a thread alive for the rest of the session.
                if (hook != IntPtr.Zero)
                    NativeMethods.UnhookWindowsHookEx(hook);

                if (threadId != 0)
                    NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);

                thread.Join(150);
            }
        }

        ClearQueue();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        Stop();
    }

    private void HookThreadMain(TaskCompletionSource<bool> ready, long generation)
    {
        IntPtr installed = IntPtr.Zero;

        try
        {
            lock (_sync)
            {
                if (_disposed || generation != _generation)
                {
                    ready.TrySetResult(true);
                    return;
                }
            }

            var threadId = NativeMethods.GetCurrentThreadId();

            // Create the thread's message queue before the hook. A low-level hook is
            // delivered through that queue, and input arriving in between would be lost.
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

            unsafe
            {
                installed = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_MOUSE_LL,
                    &HookProc,
                    NativeMethods.GetModuleHandle(null),
                    0);
            }

            var startupError = installed == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;

            bool cancelled;
            lock (_sync)
            {
                cancelled = _disposed || generation != _generation;

                if (!cancelled)
                {
                    _threadId = threadId;
                    _hook = installed;
                }
            }

            ready.TrySetResult(true);

            if (installed == IntPtr.Zero)
            {
                _logger.LogWarning(
                    "Registering the low-level mouse hook failed with error {Error}", startupError);

                return;
            }

            if (cancelled)
                return;

            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessage(in message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The desktop gesture hook thread failed");
            ready.TrySetResult(true);
        }
        finally
        {
            if (installed != IntPtr.Zero)
                NativeMethods.UnhookWindowsHookEx(installed);

            lock (_sync)
            {
                if (ReferenceEquals(_thread, Thread.CurrentThread))
                {
                    _thread = null;
                    _threadId = 0;
                    _hook = IntPtr.Zero;
                }
            }

            ClearQueue();
        }
    }

    /// <summary>
    /// The hook callback. It reads the press and nothing else: everything that
    /// could take any time is done on the worker the press is handed to.
    /// </summary>
    /// <remarks>
    /// Nothing may escape: this runs as a native callback on the input path of the
    /// whole machine, where an exception is not an error report but the end of the
    /// process. A press that cannot be read is dropped instead.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var watcher = Volatile.Read(ref _watcher);

            if (watcher is not null && nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                watcher.QueuePress(data.Point, data.Time);
            }
        }
        catch (Exception)
        {
        }

        // The hook handle is documented as ignored by the system here, and passing
        // zero keeps the callback free of any shared state it would have to read.
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void QueuePress(NativeMethods.POINT point, uint timeMs)
    {
        if (Interlocked.Increment(ref _queuedPresses) > MaximumQueuedPresses)
        {
            Interlocked.Decrement(ref _queuedPresses);
            return;
        }

        _presses.Enqueue(new QueuedPress(
            Interlocked.Read(ref _generation), point, timeMs));

        ScheduleDrain();
    }

    /// <summary>
    /// Starts a worker unless one is already draining. The flag is what keeps a
    /// burst of presses to a single drain, so the double-click detector — which is
    /// not synchronised — is only ever fed by one worker.
    /// </summary>
    private void ScheduleDrain()
    {
        if (Interlocked.Exchange(ref _drainScheduled, 1) != 0)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(static watcher => watcher.Drain(), this, preferLocal: false);
    }

    private void Drain()
    {
        try
        {
            while (_presses.TryDequeue(out var press))
            {
                Interlocked.Decrement(ref _queuedPresses);

                try
                {
                    ProcessPress(press);
                }
                catch (Exception ex)
                {
                    // Nothing may escape a work item queued the unsafe way: the pool
                    // rethrows it with no handler and the process goes down with it. A
                    // gesture that could not be answered is worth far less than the
                    // session it would take with it.
                    _logger.LogWarning(ex, "A desktop double-click could not be answered");
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _drainScheduled, 0);

            // A press that arrived while the worker was finishing would otherwise sit
            // in the queue until the next one.
            if (!_presses.IsEmpty)
                ScheduleDrain();
        }
    }

    private void ProcessPress(QueuedPress press)
    {
        var detector = _detector;
        if (detector is null || press.Generation != Interlocked.Read(ref _generation))
            return;

        // The expensive half, deliberately not in the callback: this may have to wait
        // for Explorer, and the hook must not.
        var isBackdrop = DesktopIconHitTest.IsBlankDesktopPoint(press.Point.X, press.Point.Y);

        if (!detector.Accept(new PointerSample(press.Point.X, press.Point.Y, press.TimeMs, isBackdrop)))
            return;

        if (press.Generation != Interlocked.Read(ref _generation))
            return;

        Raise();
    }

    private void Raise()
    {
        // The product answers the gesture on its UI thread, and this is not it.
        try
        {
            Dispatcher.UIThread.Post(() => BackdropDoubleClicked?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            // A dispatcher that is shutting down, which is a gesture with nowhere to go
            // rather than a reason to fail a worker.
            _logger.LogDebug(ex, "The desktop gesture could not be delivered to the UI thread");
        }
    }

    private void ClearQueue()
    {
        while (_presses.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _queuedPresses, 0);
    }

    /// <summary>One press waiting for the hit test, and the watcher that heard it.</summary>
    private readonly record struct QueuedPress(long Generation, NativeMethods.POINT Point, uint TimeMs);
}
#endif
