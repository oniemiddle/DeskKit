using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace DeskKit.App.Services;

/// <summary>
/// Applies the light/dark preference and hands out the widget card surface
/// brush that matches it.
/// </summary>
public sealed class ThemeService
{
    private static readonly IBrush DarkCard = new SolidColorBrush(Color.Parse("#F01B1B22"));

    private static readonly IBrush LightCard = new SolidColorBrush(Color.Parse("#F5FAFAFC"));

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
    public static IBrush CardBrush =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? DarkCard : LightCard;
}
