using DeskKit.App.Services;
using DeskKit.Platform;

namespace DeskKit.App.Shell;

/// <summary>
/// What the theme and the surface material do to the widgets that are already on screen.
/// </summary>
/// <remarks>
/// Only appearance. It deliberately does not know about the language: a culture change
/// reaches the shell as a separate concern because it also has to reletter menus and
/// lists, and folding the two together is what made the class this came from hard to
/// reason about.
/// <para>
/// The material is resolved once, before any window exists, because it decides the
/// layout of every window; this only repaints what is already there when the preference
/// changes.
/// </para>
/// </remarks>
internal sealed class AppearanceController(ThemeService themes, WidgetMaterial material)
{
    /// <summary>Applies the stored theme preference to the application.</summary>
    public void ApplyTheme(string theme) => themes.Apply(theme);

    /// <summary>
    /// Repaints every live card. A card painted for the other variant would be the one
    /// visibly wrong thing on screen after a theme change, because the widgets outlive
    /// the settings window that changed it.
    /// </summary>
    public void ApplyCardSurfaces(IReadOnlyList<WidgetRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        foreach (var runtime in runtimes)
            runtime.Window.CardBackground = ThemeService.CardBrushFor(material);
    }
}
