using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using DeskKit.Platform;

namespace DeskKit.App.Services;

/// <summary>
/// Applies the light/dark preference and hands out the widget card surface
/// brush that matches it.
/// </summary>
public sealed class ThemeService
{
    private static readonly IBrush DarkCard = new SolidColorBrush(Color.Parse("#F01B1B22"));

    private static readonly IBrush LightCard = new SolidColorBrush(Color.Parse("#F5FAFAFC"));

    /// <summary>
    /// The surface a card is painted with when it is sitting on a material: none.
    /// The material is the background, so anything painted over it can only dilute
    /// it — and since how much survives is exactly <c>1 - alpha</c>, any tint is a
    /// direct subtraction from the surface the material is there to provide. The
    /// content over it follows the theme variant instead, which is what makes a
    /// bare material readable; see <c>WidgetTheme.axaml</c>.
    /// </summary>
    private static readonly IBrush MaterialSurface = Brushes.Transparent;

    public void Apply(string theme)
    {
        if (Application.Current is not { } application)
            return;

        application.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>The surface a widget card is painted with, following the live theme.</summary>
    public static IBrush CardBrush => CardBrushFor(WidgetMaterial.None);

    /// <summary>
    /// The surface for a card on the given material. Over a material the card
    /// paints nothing, so that what is seen is the material itself rather than the
    /// material diluted by a tint.
    /// </summary>
    public static IBrush CardBrushFor(WidgetMaterial material)
    {
        if (MaterialPolicy.FillsWindow(material))
            return MaterialSurface;

        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? DarkCard : LightCard;
    }
}
