// Compiled only into the Windows target framework.
#if WINDOWS
using System.Runtime.InteropServices;

namespace DeskKit.Platform.Interop;

/// <summary>
/// Native entry points used by the desktop layer. Every member is Windows-only,
/// which is why this file is compiled into the Windows target framework alone.
/// </summary>
/// <remarks>
/// Declared with <see cref="LibraryImportAttribute"/> rather than
/// <see cref="DllImportAttribute"/>, so the marshalling — the part that is easy to get
/// wrong and silent when it is — is generated at compile time instead of written by
/// hand. The trade is that a few signatures cannot be expressed: a <c>bool</c> has to
/// say which width of BOOL it means, strings have to name the W entry point, and the
/// two calls that fill a caller's buffer take a span rather than a
/// <see cref="System.Text.StringBuilder"/>.
/// </remarks>
internal static partial class NativeMethods
{
    // Window style access. GetWindowLongPtrW/SetWindowLongPtrW only exist in the
    // 64-bit user32, so on a 32-bit process we must use the 32-bit variants.
    private static bool Is64Bit => IntPtr.Size == 8;

    internal static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        Is64Bit
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));

    internal static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        Is64Bit
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong32(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsZoomed(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetTopWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true)]
    private static unsafe partial int GetClassName(IntPtr hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true)]
    private static unsafe partial int GetWindowText(IntPtr hWnd, char* lpString, int nMaxCount);

    /// <summary>The window's class name, or an empty string when it cannot be read.</summary>
    internal static unsafe string ClassNameOf(IntPtr hWnd)
    {
        const int Length = 256;
        var buffer = stackalloc char[Length];
        var written = GetClassName(hWnd, buffer, Length);

        return written > 0 ? new string(buffer, 0, Math.Min(written, Length)) : string.Empty;
    }

    /// <summary>The window's title, or an empty string when it cannot be read.</summary>
    internal static unsafe string WindowTextOf(IntPtr hWnd)
    {
        const int Length = 256;
        var buffer = stackalloc char[Length];
        var written = GetWindowText(hWnd, buffer, Length);

        return written > 0 ? new string(buffer, 0, Math.Min(written, Length)) : string.Empty;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Sends a message. Named <c>SendMessageW</c> rather than the macro name: there is
    /// no <c>SendMessage</c> export, and a source-generated declaration does not get
    /// the A/W suffix appended for it the way a hand-written one did.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    /// <summary>
    /// Registers the calling window to receive shell hook notifications
    /// (WM_SHELLHOOK). Not declared in modern SDK headers; still exported by
    /// user32 on Windows 10/11. A failure here is non-fatal.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterShellHookWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeregisterShellHookWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport("gdi32.dll")]
    internal static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true)]
    internal static partial IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DrawIconEx(
        IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight,
        uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    // ---- Desktop Window Manager ------------------------------------------

    /// <summary>
    /// Writes a window attribute. Returns a non-zero HRESULT when the attribute is
    /// not understood, which is how a Windows version that predates it reports
    /// itself, so callers must check the result rather than assume success.
    /// </summary>
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Reads a window attribute back; used to confirm a backdrop took effect.</summary>
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(
        IntPtr hwnd, int attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    /// <summary>
    /// Mirror of the native WINDOWPOS structure pointed to by the lParam of
    /// WM_WINDOWPOSCHANGING. Field order and packing must match exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }
}
#endif
