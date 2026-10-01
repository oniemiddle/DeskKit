namespace DeskKit.Platform;

/// <summary>
/// The material a widget window's surface is made of.
/// <para>
/// Widgets are chrome-less, so the surface behind their content is the whole
/// window rather than a themed backdrop inside it. Where the platform can supply
/// one, that surface is a system material — the desktop shows through it, blurred
/// and tinted — instead of a flat colour that has to be repainted whenever the
/// wallpaper changes.
/// </para>
/// </summary>
public enum WidgetMaterial
{
    /// <summary>No material: the window is fully transparent and the card is its own surface.</summary>
    None,

    /// <summary>Windows 11 Mica, which samples the desktop wallpaper.</summary>
    Mica,

    /// <summary>Windows 11 Mica Alt, a more contrasted variant of <see cref="Mica"/>.</summary>
    MicaAlt,

    /// <summary>Windows Acrylic, which blurs whatever is actually behind the window.</summary>
    Acrylic,

    /// <summary>
    /// The macOS equivalent (Liquid Glass). Declared so the choice travels through
    /// the same seam, but not implemented yet; it resolves to
    /// <see cref="None"/> rather than to a material that would not render.
    /// </summary>
    LiquidGlass,
}
