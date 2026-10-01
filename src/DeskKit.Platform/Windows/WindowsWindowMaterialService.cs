using Avalonia.Controls;
using DeskKit.Platform.Interop;

namespace DeskKit.Platform.Windows;

/// <summary>
/// Gives widget windows a Windows 11 system backdrop (Mica by default).
/// <para>
/// The backdrop is requested through Avalonia's transparency hint, which is the
/// path that makes the framework create the window in a way the desktop compositor
/// can draw behind. Widget windows are transparent windows rendered through
/// DirectComposition, and it is tempting to assume that rules a backdrop out — a
/// transparent window has no redirection bitmap, so there is no obvious surface to
/// compose a backdrop into. It does not: measured on Windows 11 build 26200, a
/// borderless window with <c>WS_EX_NOREDIRECTIONBITMAP</c> set and no content at
/// all does show Mica, and an otherwise identical transparent window shows nothing.
/// </para>
/// <para>
/// The exact variant is then named through <c>DWMWA_SYSTEMBACKDROP_TYPE</c>, because
/// the hint only expresses "a backdrop" and cannot distinguish Mica from Mica Alt.
/// Writing it is harmless where the hint already produced the same thing, and is
/// what makes Mica Alt reachable at all.
/// </para>
/// </summary>
public sealed class WindowsWindowMaterialService : IWindowMaterialService
{
    // DWMWINDOWATTRIBUTE values.
    private const int DwmwaWindowCornerPreference = Win32.DWMWA_WINDOW_CORNER_PREFERENCE;
    private const int DwmwaSystemBackdropType = Win32.DWMWA_SYSTEMBACKDROP_TYPE;

    /// <summary>
    /// The attribute Windows 11 21H2 shipped before the backdrop type existed; it
    /// can only turn Mica on and off, and is a boolean.
    /// </summary>
    private const int DwmwaMicaEffect = Win32.DWMWA_MICA_EFFECT;

    // DWM_WINDOW_CORNER_PREFERENCE values.
    private const int DwmwcpRound = Win32.DWMWCP_ROUND;

    // DWM_SYSTEMBACKDROP_TYPE values.
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;
    private const int DwmsbtTabbedWindow = 4;

    private readonly int _build;

    public WindowsWindowMaterialService()
        : this(Environment.OSVersion.Version.Build)
    {
    }

    /// <summary>Test seam: the Windows build decides which materials are reachable.</summary>
    public WindowsWindowMaterialService(int windowsBuild)
    {
        _build = windowsBuild;
    }

    public bool IsSupported =>
        MaterialPolicy.Resolve(Default, isWindows: true, _build) != WidgetMaterial.None;

    /// <summary>
    /// Mica rather than Acrylic: a widget sits on the desktop for hours, and
    /// Acrylic resamples whatever happens to be behind the window, so it would
    /// flicker as other windows move over it. Mica samples the wallpaper, which is
    /// stable, and that is the surface a desktop widget should look like.
    /// </summary>
    public WidgetMaterial Default => WidgetMaterial.Mica;

    public WidgetMaterial Resolve(WidgetMaterial requested) =>
        MaterialPolicy.Resolve(requested, isWindows: true, _build);

    public void Prepare(Window window, WidgetMaterial material)
    {
        if (material == WidgetMaterial.None)
        {
            // Deliberately left alone. The window is declared transparent, and
            // naming a level here would take that away: the classic path relies on
            // the per-pixel alpha the transparency hint gives it, and asking for
            // "no transparency" would make the window opaque and put a visible
            // rectangle around the card.
            return;
        }

        // What the hint decides is how the window is created: requesting a backdrop
        // here is what stops Avalonia from giving the window the redirection-bitmap
        // -less surface it uses for per-pixel alpha, without which the compositor
        // has nothing to draw a backdrop into.
        window.TransparencyLevelHint = [ToTransparencyLevel(material)];

        // The exact variant is then pinned through the DWM, because the hint only
        // expresses "a backdrop" and cannot tell Mica from Mica Alt. On a build
        // without the attribute this fails harmlessly and the hint stands.
        DeferUntilHandle(window, () =>
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
                return;

            SetAttribute(handle, DwmwaSystemBackdropType, BackdropTypeFor(material));

            // The window is now the widget's surface, so its corners belong to the
            // desktop rather than to a card drawn inside it. Asking explicitly
            // keeps the shape the same on a build whose default is square.
            SetAttribute(handle, DwmwaWindowCornerPreference, DwmwcpRound);
        });
    }

    public bool IsActive(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return false;

        if (_build >= MaterialPolicy.SystemBackdropMinimumBuild)
        {
            if (NativeMethods.DwmGetWindowAttribute(
                    handle, DwmwaSystemBackdropType, out var backdrop, sizeof(int)) == 0)
            {
                return backdrop != DwmsbtNone;
            }
        }

        if (_build >= MaterialPolicy.MicaEffectMinimumBuild
            && NativeMethods.DwmGetWindowAttribute(
                handle, DwmwaMicaEffect, out var mica, sizeof(int)) == 0)
        {
            return mica != 0;
        }

        return false;
    }

    private static WindowTransparencyLevel ToTransparencyLevel(WidgetMaterial material) =>
        material switch
        {
            WidgetMaterial.Mica or WidgetMaterial.MicaAlt => WindowTransparencyLevel.Mica,
            WidgetMaterial.Acrylic => WindowTransparencyLevel.AcrylicBlur,
            _ => WindowTransparencyLevel.None,
        };

    private static void SetAttribute(IntPtr handle, int attribute, int value)
    {
        var current = value;
        NativeMethods.DwmSetWindowAttribute(handle, attribute, ref current, sizeof(int));
    }

    /// <summary>
    /// Runs <paramref name="action"/> once the window has a handle. Window
    /// attributes are cosmetic and orthogonal to how the window is created, so it
    /// is not worth making the caller wait for a handle it does not need.
    /// </summary>
    private static void DeferUntilHandle(Window window, Action action)
    {
        if (window.TryGetPlatformHandle() is not null)
            action();
        else
            window.Opened += (_, _) => action();
    }

    /// <summary>
    /// The backdrop a material asks the compositor for. Exposed so a diagnostic can
    /// report the mapping it actually used.
    /// <para>
    /// Mica Alt is a real backdrop type, but on a borderless window that is not
    /// carrying a window frame it renders identically to Mica — measured on build
    /// 26200, the two are pixel-for-pixel the same. It is kept because the request
    /// is genuine and costs nothing, not because it currently looks different.
    /// </para>
    /// </summary>
    public static int BackdropTypeFor(WidgetMaterial material) =>
        material switch
        {
            WidgetMaterial.Mica => DwmsbtMainWindow,
            WidgetMaterial.MicaAlt => DwmsbtTabbedWindow,
            WidgetMaterial.Acrylic => DwmsbtTransientWindow,
            _ => DwmsbtNone,
        };
}
