
namespace DeskKit.Runtime;

/// <summary>
/// What the theme and the surface material do to the widgets that are already on screen.
/// </summary>
/// <remarks>
/// Only appearance. It deliberately does not know about the language: a culture change
/// reaches the shell as a separate concern because it also has to reletter menus and
/// lists, and folding the two together is what made the class this came from hard to
/// reason about.
/// </remarks>
internal sealed class AppearanceController(ThemeService themes)
{
    /// <summary>Applies the stored theme preference to the application.</summary>
    public void ApplyTheme(string theme) => themes.Apply(theme);

    /// <summary>
    /// Repaints every live card. A card painted for the other variant would be the one
    /// visibly wrong thing on screen after a theme change, because the widgets outlive
    /// the settings window that changed it.
    /// </summary>
    /// <remarks>
    /// The material is asked of each window rather than carried in from the shell. The
    /// window was built for the material it has, so this cannot disagree with what the
    /// window is actually made of - and there is no second copy of the answer to keep
    /// in step.
    /// </remarks>
    public void ApplyCardSurfaces(IReadOnlyList<WidgetRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        foreach (var runtime in runtimes)
            runtime.Window.CardBackground = ThemeService.CardBrushFor(runtime.Window.Material);
    }
}
