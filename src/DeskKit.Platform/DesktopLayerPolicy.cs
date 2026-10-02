using DeskKit.Core.Abstractions;

namespace DeskKit.Platform;

/// <summary>
/// A window-position change request as seen by <c>WM_WINDOWPOSCHANGING</c>.
/// Sizes are physical pixels.
/// </summary>
/// <param name="InsertAfter">Requested z-order insertion point.</param>
/// <param name="Cx">Requested width in physical pixels.</param>
/// <param name="Cy">Requested height in physical pixels.</param>
/// <param name="Flags">A combination of the <c>Swp*</c> constants.</param>
public readonly record struct WindowPosRequest(IntPtr InsertAfter, int Cx, int Cy, uint Flags);

/// <summary>Result of applying <see cref="DesktopLayerPolicy"/> to a request.</summary>
public readonly record struct WindowPosDecision(
    IntPtr InsertAfter, int Cx, int Cy, uint Flags, bool Modified);

/// <summary>Per-window settings that drive <see cref="DesktopLayerPolicy.DecideWindowPos"/>.</summary>
public sealed record WindowPosConstraints(
    bool ForceBottom,
    bool RejectHide,
    double NormalWidthPx,
    double NormalHeightPx,
    /// <summary>
    /// How tall a request may be before it is considered a real resize rather
    /// than a collapse to the caption icon. Physical pixels, so callers must
    /// scale <see cref="DesktopLayerPolicy.IconRectMaxHeight"/> by the window's
    /// render scaling.
    /// </summary>
    double IconRectMaxHeightPx = DesktopLayerPolicy.IconRectMaxHeight);

/// <summary>
/// The decision rules that keep a widget glued to the desktop.
/// <para>
/// This type is deliberately free of any Win32 or UI dependency: it is pure
/// logic so the rules can be pinned by unit tests without creating a window.
/// The WndProc hook in <c>DeskKit.Platform.Windows</c> is a thin adapter over it.
/// </para>
/// <para>
/// The strategy is "bottom-most pinning" rather than reparenting the window
/// into Explorer's wallpaper <c>WorkerW</c>. A reparented window becomes a
/// CHILD window, and DWM stops composing child windows the way it composes
/// top-level ones. Avalonia renders transparent windows through
/// <c>WS_EX_NOREDIRECTIONBITMAP</c> and DirectComposition, so on many
/// GPU/driver combinations a reparented widget paints as a solid black
/// rectangle: alive, hit-testable and invisible. Because that failure is
/// driver-dependent, the default is to keep a normal top-level window and
/// intercept every code path that could hide, minimise or un-pin it.
/// </para>
/// </summary>
public static class DesktopLayerPolicy
{
    // ---- SetWindowPos flags (mirrored so this type stays dependency free) --

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpHideWindow = 0x0080;

    /// <summary>The "place at the very bottom of the z-order" pseudo-handle.</summary>
    public static readonly IntPtr HwndBottom = new(1);

    // ---- WM_SYSCOMMAND opcodes -------------------------------------------

    public const long ScMinimize = 0xF020;
    public const long ScMaximize = 0xF030;
    public const long ScRestore = 0xF120;

    /// <summary>
    /// Undocumented opcode the Windows 11 shell sends during a "Show Desktop"
    /// cycle. It is treated as a minimise-class command because the visible
    /// result is the same: the window goes away.
    /// </summary>
    public const long ScDesktop = 0xF130;

    /// <summary>Masks off the modifier-key bits that share the low nibble of wParam.</summary>
    public const long ScCommandMask = 0xFFF0;

    /// <summary><c>WM_SIZE</c> wParam value meaning the window was minimised.</summary>
    public const int SizeMinimized = 1;

    /// <summary>
    /// Height in physical pixels at 100% scaling at or below which a resize is
    /// treated as the caption icon rect rather than a genuine user resize. A
    /// themed caption is about 28px tall, so 32 leaves a little headroom.
    /// <para>
    /// This is a per-monitor-independent figure: callers multiply it by the
    /// window's render scaling, because at 200% scaling a caption icon is about
    /// 56 physical pixels tall and a fixed threshold would miss it entirely.
    /// </para>
    /// </summary>
    public const int IconRectMaxHeight = 32;

    /// <summary>
    /// True when a <c>WM_SYSCOMMAND</c> wParam is one of the minimise-class
    /// opcodes: <c>SC_MINIMIZE</c>, <c>SC_MAXIMIZE</c> or <c>SC_DESKTOP</c>.
    /// </summary>
    public static bool IsMinimizeSysCommand(long wParam) =>
        (wParam & ScCommandMask) is ScMinimize or ScMaximize or ScDesktop;

    /// <summary>True when a <c>WM_SYSCOMMAND</c> wParam is <c>SC_RESTORE</c>.</summary>
    public static bool IsRestoreSysCommand(long wParam) =>
        (wParam & ScCommandMask) == ScRestore;

    /// <summary>True when a <c>WM_SIZE</c> wParam reports the minimised state.</summary>
    public static bool IsSizeMinimized(long wParam) => wParam == SizeMinimized;

    /// <summary>
    /// Heuristic for "this new size is the icon rect a minimised window gets".
    /// <para>
    /// All sizes are physical pixels, including <paramref name="iconRectMaxHeightPx"/>.
    /// Comparing physical pixels against Avalonia's logical (DIP) sizes would
    /// misclassify genuine resizes on high-DPI displays, so callers convert with
    /// the window's render scaling first.
    /// </para>
    /// <para>
    /// The thresholds are deliberately conservative: a widget is never shorter
    /// than its configured minimum (80px and up), so a request for a rect that
    /// is caption-height, no more than a quarter of the normal height, and
    /// narrower than three quarters of the normal width cannot be a real
    /// resize.
    /// </para>
    /// </summary>
    public static bool IsMinimizeSize(
        int cx,
        int cy,
        double normalWidthPx,
        double normalHeightPx,
        double iconRectMaxHeightPx = IconRectMaxHeight)
    {
        if (normalWidthPx <= 0 || normalHeightPx <= 0)
            return false;

        if (cy > iconRectMaxHeightPx)
            return false;

        if (cy > normalHeightPx * 0.25)
            return false;

        if (cx >= normalWidthPx * 0.75)
            return false;

        return true;
    }

    /// <summary>
    /// Applies the pinning rules to an incoming window-position change.
    /// Every rule is a no-op when it does not apply, so the result can be
    /// written back to the native <c>WINDOWPOS</c> unconditionally.
    /// </summary>
    public static WindowPosDecision DecideWindowPos(
        WindowPosRequest request, WindowPosConstraints constraints)
    {
        var insertAfter = request.InsertAfter;
        var cx = request.Cx;
        var cy = request.Cy;
        var flags = request.Flags;
        var modified = false;

        // Force the z-order to the bottom unless the caller explicitly asked
        // for "no z-order change" (a plain move or resize).
        if (constraints.ForceBottom && (flags & SwpNoZOrder) == 0)
        {
            if (insertAfter != HwndBottom)
                modified = true;

            insertAfter = HwndBottom;
        }

        // A desktop widget is never hidden; turn a hide request into a show.
        if (constraints.RejectHide && (flags & SwpHideWindow) != 0)
        {
            flags = (flags & ~SwpHideWindow) | SwpShowWindow;
            modified = true;
        }

        // Reject the collapse-to-icon rect that every minimise path eventually
        // funnels through as a size change.
        if ((flags & SwpNoSize) == 0
            && IsMinimizeSize(cx, cy, constraints.NormalWidthPx, constraints.NormalHeightPx,
                constraints.IconRectMaxHeightPx))
        {
            cx = (int)Math.Round(constraints.NormalWidthPx);
            cy = (int)Math.Round(constraints.NormalHeightPx);
            flags &= ~SwpFrameChanged; // we are not really changing the frame
            modified = true;
        }

        return new WindowPosDecision(insertAfter, cx, cy, flags, modified);
    }
}
