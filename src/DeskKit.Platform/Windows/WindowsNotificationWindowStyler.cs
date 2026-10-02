// Compiled only into the Windows target framework.
#if WINDOWS
using Avalonia.Controls;
using DeskKit.Platform.Interop;

namespace DeskKit.Platform.Windows;

/// <summary>
/// The Windows implementation of <see cref="INotificationWindowStyler"/>.
/// </summary>
/// <remarks>
/// Avalonia's <c>ShowInTaskbar</c> and <c>ShowActivated</c> cover less than they
/// sound like they do. A window with both set still turns up in Alt+Tab, and still
/// becomes the active window when it is clicked.
/// <c>WS_EX_TOOLWINDOW</c> and <c>WS_EX_NOACTIVATE</c> are what actually decide it.
/// Clicking still works: a window that may not be activated still receives the
/// mouse.
/// </remarks>
public sealed class WindowsNotificationWindowStyler : INotificationWindowStyler
{
    private static readonly IntPtr HwndTopmost = new(-1);

    /// <summary>
    /// True by definition: this implementation only exists in the Windows target
    /// framework, which is where the styles it sets are compiled in.
    /// </summary>
    public bool IsSupported => true;

    public void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            return;

        var style = NativeMethods.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        style |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;

        NativeMethods.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(style));

        // Changing the extended style makes the window manager recalculate the frame,
        // and the window comes back out of the topmost band — measured on a real
        // desktop, where the notice ended up behind a maximised editor. Being on top
        // has to be reasserted afterwards.
        NativeMethods.SetWindowPos(
            hwnd,
            HwndTopmost,
            0, 0, 0, 0,
            DesktopLayerPolicy.SwpNoMove | DesktopLayerPolicy.SwpNoSize | DesktopLayerPolicy.SwpNoActivate);
    }
}
#endif
