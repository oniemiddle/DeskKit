namespace DeskKit.Platform;

/// <summary>
/// The rules that decide which material a widget window can actually be given.
/// <para>
/// Like <see cref="DesktopLayerPolicy"/> this type is deliberately free of any
/// Win32 or UI dependency, so the rules can be pinned by unit tests without a
/// window and without a particular Windows build. The platform services are thin
/// adapters over it.
/// </para>
/// </summary>
public static class MaterialPolicy
{
    /// <summary>
    /// First Windows 11 build (22H2) with the documented backdrop attribute,
    /// <c>DWMWA_SYSTEMBACKDROP_TYPE</c>. It is the only version that can express
    /// Mica, Mica Alt and Acrylic as distinct choices.
    /// </summary>
    public const int SystemBackdropMinimumBuild = 22621;

    /// <summary>
    /// First Windows 11 build (21H2), which has only the undocumented
    /// <c>DWMWA_MICA_EFFECT</c>. Mica can be turned on there; nothing else can.
    /// </summary>
    public const int MicaEffectMinimumBuild = 22000;

    /// <summary>
    /// The highest material that can be honoured on Windows of the given build.
    /// An unsupported request falls back to a supported one rather than to a
    /// material that would silently render as a flat card, and only falls back to
    /// <see cref="WidgetMaterial.None"/> once no material is possible at all.
    /// </summary>
    public static WidgetMaterial Resolve(WidgetMaterial requested, bool isWindows, int windowsBuild)
    {
        if (requested == WidgetMaterial.None)
            return WidgetMaterial.None;

        if (!isWindows)
        {
            // Liquid Glass is the only material declared for a non-Windows host,
            // and macOS support is not implemented yet.
            return WidgetMaterial.None;
        }

        var supportsSystemBackdrop = windowsBuild >= SystemBackdropMinimumBuild;

        return requested switch
        {
            WidgetMaterial.Mica or WidgetMaterial.MicaAlt =>
                supportsSystemBackdrop || windowsBuild >= MicaEffectMinimumBuild
                    ? requested
                    : WidgetMaterial.None,

            // Acrylic has no equivalent before the backdrop attribute existed.
            WidgetMaterial.Acrylic =>
                supportsSystemBackdrop ? WidgetMaterial.Acrylic : WidgetMaterial.None,

            WidgetMaterial.LiquidGlass => WidgetMaterial.None,

            _ => WidgetMaterial.None,
        };
    }

    /// <summary>
    /// True when the material is rendered by the window itself, so the window is
    /// the widget's surface.
    /// <para>
    /// This is what decides the layout: a material fills the window's whole
    /// rectangle, so a card inset from it would sit on a visible plate of material
    /// instead of being the thing the material shows through. With a material the
    /// card therefore fills the window and the platform draws the rounded corners
    /// and the drop shadow that the inset and the card's own shadow used to
    /// provide.
    /// </para>
    /// </summary>
    public static bool FillsWindow(WidgetMaterial material) => material != WidgetMaterial.None;
}
