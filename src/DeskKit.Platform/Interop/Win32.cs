// Compiled only into the Windows target framework.
#if WINDOWS
namespace DeskKit.Platform.Interop;

/// <summary>
/// Win32 messages, styles and handles used by the desktop layer.
/// <para>
/// SetWindowPos flags, WM_SYSCOMMAND opcodes and the minimise-size thresholds
/// deliberately live on <see cref="DesktopLayerPolicy"/> instead, because that
/// type is public and the rules it encodes are part of the desktop-layer
/// contract.
/// </para>
/// </summary>
internal static class Win32
{
    // ---- Window messages -------------------------------------------------

    internal const uint WM_SIZE = 0x0005;
    internal const uint WM_ACTIVATEAPP = 0x001C;
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_WINDOWPOSCHANGING = 0x0046;
    internal const uint WM_DISPLAYCHANGE = 0x007E;
    internal const uint WM_SYSCOMMAND = 0x0112;

    // ---- ShowWindow commands --------------------------------------------

    internal const int SW_HIDE = 0;
    internal const int SW_SHOWMINIMIZED = 2;
    internal const int SW_SHOWNOACTIVATE = 4;

    // ---- GetWindow commands ----------------------------------------------

    internal const uint GW_HWNDNEXT = 2;
    internal const uint GW_HWNDPREV = 3;

    // ---- Window long indices ---------------------------------------------

    internal const int GWL_EXSTYLE = -20;

    // ---- Extended window styles ------------------------------------------

    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_TOPMOST = 0x00000008;
    internal const int WS_EX_APPWINDOW = 0x00040000;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    internal const int WS_EX_LAYERED = 0x00080000;

    // ---- DWM window attributes -------------------------------------------

    /// <summary>Rounds the window's corners. Windows 11 and later.</summary>
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>
    /// Names the system backdrop (Mica, Acrylic) the compositor draws behind the
    /// window. Windows 11 22H2 and later; earlier builds return a failure.
    /// </summary>
    internal const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>
    /// The pre-22H2 way of asking for Mica, which can only turn it on and off.
    /// Windows 11 21H2 only.
    /// </summary>
    internal const int DWMWA_MICA_EFFECT = 1029;

    /// <summary><c>DWM_WINDOW_CORNER_PREFERENCE</c>: let the system decide.</summary>
    internal const int DWMWCP_DEFAULT = 0;

    /// <summary><c>DWM_WINDOW_CORNER_PREFERENCE</c>: round the corners.</summary>
    internal const int DWMWCP_ROUND = 2;

    // ---- GDI / screen capture (self-test only) ----------------------------

    internal const int SRCCOPY = 0x00CC0020;
    internal const int DIB_RGB_COLORS = 0;
    internal const uint BI_RGB = 0;

    // ---- Shell icon extraction --------------------------------------------

    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;

    /// <summary>Draws the icon's image and mask, which is what filled icons need.</summary>
    internal const uint DI_NORMAL = 0x0003;

    internal const int SM_CXICON = 11;
    internal const int SM_CYICON = 12;
}
#endif
