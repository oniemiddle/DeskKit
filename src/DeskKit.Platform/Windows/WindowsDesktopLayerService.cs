using System.Runtime.InteropServices;
using Avalonia.Controls;
using DeskKit.Platform.Interop;

namespace DeskKit.Platform.Windows;

/// <summary>
/// Windows implementation of <see cref="IDesktopLayerService"/>.
/// <para>
/// A widget window stays an ordinary top-level window and is kept at the very
/// bottom of the z-order by the rules in <see cref="DesktopLayerPolicy"/>.
/// </para>
/// </summary>
/// <remarks>
/// All members are expected to be called from the UI thread: the per-window
/// state (including the suspend counter) is not synchronised.
/// </remarks>
public sealed class WindowsDesktopLayerService : IDesktopLayerService
{
    private static readonly Lazy<uint> ShellHookMessageId =
        new(() => NativeMethods.RegisterWindowMessage("SHELLHOOK"));

    private readonly Dictionary<IntPtr, PinnedWindowState> _states = [];

    public bool IsSupported => OperatingSystem.IsWindows();

    public void Attach(Window window, DesktopLayerOptions options)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hwnd = GetHandle(window);
        if (hwnd == IntPtr.Zero || _states.ContainsKey(hwnd))
            return;

        var state = new PinnedWindowState(hwnd, window, options);
        _states[hwnd] = state;

        // The styles callback only runs when Avalonia recomputes the window
        // properties, so register it for later and apply the styles now.
        state.StylesCallback = state.OnWindowStyles;
        Win32Properties.AddWindowStylesCallback(window, state.StylesCallback);

        state.HookCallback = state.OnWndProc;
        Win32Properties.AddWndProcHookCallback(window, state.HookCallback);

        state.ApplyRequestedStyles();
        state.SyncNormalSize();
        state.RegisterShellHook(ShellHookMessageId.Value);
        PinToBottom(hwnd);
    }

    public void Detach(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hwnd = GetHandle(window);
        if (hwnd == IntPtr.Zero || !_states.Remove(hwnd, out var state))
            return;

        if (state.HookCallback is not null)
            Win32Properties.RemoveWndProcHookCallback(window, state.HookCallback);

        if (state.StylesCallback is not null)
            Win32Properties.RemoveWindowStylesCallback(window, state.StylesCallback);

        if (state.ShellHookRegistered)
            NativeMethods.DeregisterShellHookWindow(hwnd);

        state.ClearRequestedStyles();
    }

    public void Reassert(Window window)
    {
        if (TryGetState(window, out var state))
            PinToBottom(state.Hwnd);
    }

    public void SyncNormalSize(Window window)
    {
        if (TryGetState(window, out var state))
            state.SyncNormalSize();
    }

    public void SetVisible(Window window, bool visible)
    {
        // Suspend the hook so an intentional hide is not turned back into a show.
        using (SuspendPinning(window))
        {
            if (visible)
                window.Show();
            else
                window.Hide();
        }

        if (visible)
            Reassert(window);
    }

    public IDisposable SuspendPinning(Window window)
    {
        if (!TryGetState(window, out var state))
            return NoopScope.Instance;

        state.SuspendCount++;
        return new SuspendScope(state);
    }

    private bool TryGetState(Window window, out PinnedWindowState state)
    {
        state = null!;
        if (!OperatingSystem.IsWindows())
            return false;

        var hwnd = GetHandle(window);
        return hwnd != IntPtr.Zero && _states.TryGetValue(hwnd, out state!);
    }

    private static IntPtr GetHandle(Window window) =>
        window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>Decrements the suspend counter when the caller's scope ends.</summary>
    private sealed class SuspendScope(PinnedWindowState state) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            state.SuspendCount--;
        }
    }

    private static void PinToBottom(IntPtr hwnd) =>
        NativeMethods.SetWindowPos(
            hwnd,
            DesktopLayerPolicy.HwndBottom,
            0, 0, 0, 0,
            DesktopLayerPolicy.SwpNoMove
            | DesktopLayerPolicy.SwpNoSize
            | DesktopLayerPolicy.SwpNoActivate);

    /// <summary>Per-window pinning state and the hook that maintains it.</summary>
    private sealed class PinnedWindowState(IntPtr hwnd, Window window, DesktopLayerOptions options)
    {
        public IntPtr Hwnd { get; } = hwnd;

        public Window Window { get; } = window;

        public DesktopLayerOptions Options { get; } = options;

        /// <summary>Non-zero while an intentional show/hide is in progress.</summary>
        public int SuspendCount { get; set; }

        public double NormalWidthPx { get; private set; }

        public double NormalHeightPx { get; private set; }

        public bool ShellHookRegistered { get; private set; }

        public uint ShellHookMessageId { get; private set; }

        public WindowPosConstraints Constraints { get; private set; } =
            new(options.ForceBottom, options.RejectHide, 0, 0);

        public Win32Properties.CustomWindowStylesCallback? StylesCallback { get; set; }

        public Win32Properties.CustomWndProcHookCallback? HookCallback { get; set; }

        public void SyncNormalSize()
        {
            var scaling = Window.RenderScaling;
            if (scaling <= 0)
                scaling = 1;

            var size = Window.ClientSize;
            if (size.Width > 0)
                NormalWidthPx = size.Width * scaling;

            if (size.Height > 0)
                NormalHeightPx = size.Height * scaling;

            Constraints = new WindowPosConstraints(
                Options.ForceBottom,
                Options.RejectHide,
                NormalWidthPx,
                NormalHeightPx,

                // A caption icon is ~28px tall at 100% scaling, so the threshold
                // has to grow with the display or a high-DPI collapse is missed.
                DesktopLayerPolicy.IconRectMaxHeight * scaling);
        }

        public void RegisterShellHook(uint messageId)
        {
            if (messageId == 0)
                return;

            ShellHookMessageId = messageId;
            ShellHookRegistered = NativeMethods.RegisterShellHookWindow(Hwnd);
        }

        public (uint style, uint exStyle) OnWindowStyles(uint style, uint exStyle) =>
            (style, ApplyActivationStyle(exStyle));

        public void ApplyRequestedStyles()
        {
            var exStyle = NativeMethods.GetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE).ToInt64();
            var updated = ApplyActivationStyle((uint)exStyle);
            if (updated == (uint)exStyle)
                return;

            NativeMethods.SetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE, new IntPtr(updated));
            NativeMethods.SetWindowPos(
                Hwnd, IntPtr.Zero, 0, 0, 0, 0,
                DesktopLayerPolicy.SwpNoMove
                | DesktopLayerPolicy.SwpNoSize
                | DesktopLayerPolicy.SwpNoZOrder
                | DesktopLayerPolicy.SwpNoActivate
                | DesktopLayerPolicy.SwpFrameChanged);
        }

        public void ClearRequestedStyles()
        {
            var exStyle = NativeMethods.GetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE).ToInt64();
            var updated = exStyle & ~(long)Win32.WS_EX_NOACTIVATE;
            if (updated != exStyle)
                NativeMethods.SetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE, new IntPtr(updated));
        }

        private uint ApplyActivationStyle(uint exStyle)
        {
            if (Options.PreventActivation)
                return exStyle | (uint)Win32.WS_EX_NOACTIVATE;

            return exStyle & ~(uint)Win32.WS_EX_NOACTIVATE;
        }

        /// <summary>
        /// Runs before Avalonia's own WndProc. Returning with
        /// <paramref name="handled"/> set skips Avalonia's handling.
        /// </summary>
        public IntPtr OnWndProc(
            IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (hwnd != Hwnd || SuspendCount > 0)
                return IntPtr.Zero;

            switch (msg)
            {
                case Win32.WM_SYSCOMMAND:
                {
                    var command = wParam.ToInt64();

                    // Win+D, the taskbar minimise button and Win+Down all use
                    // this path; the Windows 11 "Show Desktop" gesture adds
                    // SC_DESKTOP on top of it.
                    if (DesktopLayerPolicy.IsMinimizeSysCommand(command))
                    {
                        handled = true;
                        NativeMethods.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
                        PinToBottom(hwnd);
                        return IntPtr.Zero;
                    }

                    // SC_RESTORE is benign, but re-assert the z-order anyway.
                    if (DesktopLayerPolicy.IsRestoreSysCommand(command))
                        PinToBottom(hwnd);

                    break;
                }

                // ShowWindow(SW_SHOWMINIMIZED) reaches DefWindowProc as
                // WM_SIZE with SIZE_MINIMIZED, by which point the window has
                // already been hidden. Restore immediately and re-pin.
                case Win32.WM_SIZE:
                    if (DesktopLayerPolicy.IsSizeMinimized(wParam.ToInt64()))
                    {
                        handled = true;
                        NativeMethods.ShowWindow(hwnd, Win32.SW_SHOWNOACTIVATE);
                        PinToBottom(hwnd);
                        return IntPtr.Zero;
                    }

                    break;

                // The catch-all: every position, size, visibility and z-order
                // change funnels through here before it happens.
                case Win32.WM_WINDOWPOSCHANGING:
                {
                    var position = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                    var decision = DesktopLayerPolicy.DecideWindowPos(
                        new WindowPosRequest(
                            position.hwndInsertAfter, position.cx, position.cy, position.flags),
                        Constraints);

                    if (decision.Modified)
                    {
                        position.hwndInsertAfter = decision.InsertAfter;
                        position.cx = decision.Cx;
                        position.cy = decision.Cy;
                        position.flags = decision.Flags;
                        Marshal.StructureToPtr(position, lParam, false);
                    }

                    break;
                }

                // Another application became active; re-pin so the widget is
                // still underneath the newly foregrounded window.
                case Win32.WM_ACTIVATEAPP:
                    if (wParam == IntPtr.Zero)
                        PinToBottom(hwnd);

                    break;

                // The desktop state changed (wallpaper swapped, display mode
                // changed, or a Show Desktop cycle that bypassed everything
                // else). A spurious re-pin costs nothing.
                case Win32.WM_SETTINGCHANGE:
                case Win32.WM_DISPLAYCHANGE:
                    PinToBottom(hwnd);
                    break;

                default:
                    if (ShellHookMessageId != 0 && msg == ShellHookMessageId)
                        PinToBottom(hwnd);

                    break;
            }

            return IntPtr.Zero;
        }
    }
}
