using System.Runtime.InteropServices;
using Avalonia;
using DeskKit.Platform.Interop;

namespace DeskKit.Platform.Windows;

/// <summary>Minimal description of a top-level window, used for diagnostics.</summary>
public sealed record WindowInfo(
    IntPtr Handle, string ClassName, string Title, bool IsVisible, bool IsMinimized, PixelRect Rect)
{
    public bool HasSize => Rect is { Width: > 0, Height: > 0 };
}

/// <summary>
/// Read-only inspection helpers used by the desktop-layer self-test. They do not
/// take part in pinning; they exist so the pinning rules can be verified on a
/// real desktop without a human looking at the screen.
/// </summary>
public static class DesktopDiagnostics
{
    public static bool TryGetWindowRect(IntPtr hwnd, out PixelRect rect)
    {
        rect = default;
        if (!NativeMethods.GetWindowRect(hwnd, out var native))
            return false;

        rect = new PixelRect(
            native.Left, native.Top, native.Width, native.Height);
        return true;
    }

    public static long GetExtendedStyle(IntPtr hwnd) =>
        NativeMethods.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();

    public static bool IsVisible(IntPtr hwnd) => NativeMethods.IsWindowVisible(hwnd);

    public static bool IsMinimized(IntPtr hwnd) => NativeMethods.IsIconic(hwnd);

    public static bool IsMaximized(IntPtr hwnd) => NativeMethods.IsZoomed(hwnd);

    public static bool IsWindow(IntPtr hwnd) => NativeMethods.IsWindow(hwnd);

    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    /// <summary>True when the window carries <c>WS_EX_NOACTIVATE</c>.</summary>
    public static bool HasNoActivateStyle(IntPtr hwnd) =>
        (GetExtendedStyle(hwnd) & Win32.WS_EX_NOACTIVATE) != 0;

    /// <summary>
    /// True when the window carries <c>WS_EX_APPWINDOW</c>, which is what puts
    /// a button on the taskbar.
    /// </summary>
    public static bool HasAppWindowStyle(IntPtr hwnd) =>
        (GetExtendedStyle(hwnd) & Win32.WS_EX_APPWINDOW) != 0;

    /// <summary>True when the window carries <c>WS_EX_TOOLWINDOW</c>.</summary>
    public static bool HasToolWindowStyle(IntPtr hwnd) =>
        (GetExtendedStyle(hwnd) & Win32.WS_EX_TOOLWINDOW) != 0;

    /// <summary>
    /// True when the window renders through DirectComposition rather than a
    /// redirection bitmap. This is the path that makes per-pixel transparency
    /// work, and the reason a window must not be reparented into the desktop's
    /// wallpaper host.
    /// </summary>
    public static bool UsesNoRedirectionBitmap(IntPtr hwnd) =>
        (GetExtendedStyle(hwnd) & Win32.WS_EX_NOREDIRECTIONBITMAP) != 0;

    /// <summary>
    /// The system backdrop the compositor has been asked to draw behind the window,
    /// read back from the DWM, or <see cref="BackdropNone"/> when it could not be
    /// read. <c>DWMSBT_AUTO</c> is reported as <see cref="BackdropAuto"/>: it means
    /// nothing was named, which is how a window whose backdrop came from a
    /// transparency hint rather than from an explicit request reports itself.
    /// </summary>
    public static int GetSystemBackdropType(IntPtr hwnd)
    {
        // Only Windows 11 22H2 and later understand the attribute at all; older
        // builds return a failure and the caller sees "none".
        return NativeMethods.DwmGetWindowAttribute(
            hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, out var backdrop, sizeof(int)) == 0
            ? backdrop
            : BackdropNone;
    }

    /// <summary>No backdrop was named; the compositor decides.</summary>
    public const int BackdropAuto = 0;

    /// <summary>No backdrop.</summary>
    public const int BackdropNone = 1;

    /// <summary>Mica.</summary>
    public const int BackdropMainWindow = 2;

    /// <summary>Acrylic.</summary>
    public const int BackdropTransientWindow = 3;

    /// <summary>Delivers a <c>WM_SYSCOMMAND</c> to the window, the way the shell does.</summary>
    public static void SendSysCommand(IntPtr hwnd, long command) =>
        NativeMethods.SendMessage(hwnd, Win32.WM_SYSCOMMAND, new IntPtr(command), IntPtr.Zero);

    /// <summary>Walks the <c>ShowWindow(SW_SHOWMINIMIZED)</c> path.</summary>
    public static void SimulateShowMinimized(IntPtr hwnd) =>
        NativeMethods.ShowWindow(hwnd, Win32.SW_SHOWMINIMIZED);

    /// <summary>Asks the window to raise itself to the top of the z-order and hide.</summary>
    public static void SimulateRaiseAndHide(IntPtr hwnd) =>
        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, 0, 0,
            DesktopLayerPolicy.SwpNoMove | DesktopLayerPolicy.SwpNoSize | DesktopLayerPolicy.SwpHideWindow);

    /// <summary>Asks the window to raise itself to the top of the z-order.</summary>
    public static void SimulateRaiseToTop(IntPtr hwnd) =>
        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, 0, 0,
            DesktopLayerPolicy.SwpNoMove | DesktopLayerPolicy.SwpNoSize);

    /// <summary>Asks for a specific size through the native resize path.</summary>
    public static void SimulateResize(IntPtr hwnd, int width, int height) =>
        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, width, height,
            DesktopLayerPolicy.SwpNoMove
            | DesktopLayerPolicy.SwpNoZOrder
            | DesktopLayerPolicy.SwpNoActivate);

    /// <summary>
    /// Every top-level window below <paramref name="hwnd"/> in the z-order,
    /// ordered from just-below-the-widget down to the bottom of the desktop.
    /// </summary>
    public static IReadOnlyList<WindowInfo> GetWindowsBelow(IntPtr hwnd)
    {
        var result = new List<WindowInfo>();
        var current = NativeMethods.GetWindow(hwnd, Win32.GW_HWNDNEXT);

        while (current != IntPtr.Zero)
        {
            result.Add(Describe(current));
            current = NativeMethods.GetWindow(current, Win32.GW_HWNDNEXT);
        }

        return result;
    }

    /// <summary>
    /// Every top-level window, ordered from the top of the z-order downwards.
    /// </summary>
    public static IReadOnlyList<WindowInfo> GetTopLevelWindows()
    {
        var result = new List<WindowInfo>();
        var current = NativeMethods.GetTopWindow(IntPtr.Zero);

        while (current != IntPtr.Zero)
        {
            result.Add(Describe(current));
            current = NativeMethods.GetWindow(current, Win32.GW_HWNDNEXT);
        }

        return result;
    }

    /// <summary>
    /// Every top-level window above <paramref name="hwnd"/> in the z-order,
    /// ordered from just-above-the-widget up to the top of the desktop. These
    /// are the windows that can cover it.
    /// </summary>
    public static IReadOnlyList<WindowInfo> GetWindowsAbove(IntPtr hwnd)
    {
        var result = new List<WindowInfo>();
        var current = NativeMethods.GetWindow(hwnd, Win32.GW_HWNDPREV);

        while (current != IntPtr.Zero)
        {
            result.Add(Describe(current));
            current = NativeMethods.GetWindow(current, Win32.GW_HWNDPREV);
        }

        return result;
    }

    /// <summary>
    /// Raises or lowers the window through the native path. Used by diagnostics
    /// that need to photograph a window the desktop layer normally keeps at the
    /// bottom of the z-order.
    /// </summary>
    public static void SetTopmost(IntPtr hwnd, bool topmost) =>
        NativeMethods.SetWindowPos(
            hwnd,
            topmost ? HwndTopmost : HwndNotTopmost,
            0, 0, 0, 0,
            DesktopLayerPolicy.SwpNoMove
            | DesktopLayerPolicy.SwpNoSize
            | DesktopLayerPolicy.SwpNoActivate);

    private static readonly IntPtr HwndTopmost = new(-1);

    private static readonly IntPtr HwndNotTopmost = new(-2);

    /// <summary>
    /// Captures a screen rectangle as top-down 32-bit BGRA pixels. Alpha is
    /// whatever the screen had, which for a BitBlt is normally zero.
    /// </summary>
    public static byte[]? CaptureBgra(PixelRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return null;

        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;

        try
        {
            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = rect.Width,
                    biHeight = -rect.Height, // negative height means top-down rows
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = Win32.BI_RGB,
                },
            };

            bitmap = NativeMethods.CreateDIBSection(
                memoryDc, ref info, Win32.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);

            if (bitmap == IntPtr.Zero)
                return null;

            previous = NativeMethods.SelectObject(memoryDc, bitmap);

            if (!NativeMethods.BitBlt(
                    memoryDc, 0, 0, rect.Width, rect.Height,
                    screenDc, rect.X, rect.Y, Win32.SRCCOPY))
            {
                return null;
            }

            var buffer = new byte[rect.Width * rect.Height * 4];
            Marshal.Copy(bits, buffer, 0, buffer.Length);
            return buffer;
        }
        finally
        {
            if (previous != IntPtr.Zero)
                NativeMethods.SelectObject(memoryDc, previous);

            if (bitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(bitmap);

            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Reads the BGRA pixel at (<paramref name="x"/>, <paramref name="y"/>) of a capture.</summary>
    public static (byte B, byte G, byte R) PixelAt(byte[] capture, int width, int x, int y)
    {
        var index = ((y * width) + x) * 4;
        return (capture[index], capture[index + 1], capture[index + 2]);
    }

    private static WindowInfo Describe(IntPtr hwnd)
    {
        var rect = NativeMethods.GetWindowRect(hwnd, out var nativeRect)
            ? new PixelRect(nativeRect.Left, nativeRect.Top, nativeRect.Width, nativeRect.Height)
            : default;

        return new WindowInfo(
            hwnd,
            NativeMethods.ClassNameOf(hwnd),
            NativeMethods.WindowTextOf(hwnd),
            NativeMethods.IsWindowVisible(hwnd),
            NativeMethods.IsIconic(hwnd),
            rect);
    }
}
