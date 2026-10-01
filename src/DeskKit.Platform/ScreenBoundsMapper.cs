using Avalonia.Controls;
using DeskKit.Core.Models;

namespace DeskKit.Platform;

/// <summary>Translates Avalonia's monitor list into the UI-free model the core uses.</summary>
public static class ScreenBoundsMapper
{
    public static IReadOnlyList<ScreenBounds> FromScreens(Screens screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        var result = new List<ScreenBounds>(screens.All.Count);
        foreach (var screen in screens.All)
        {
            var area = screen.WorkingArea;
            result.Add(new ScreenBounds(area.X, area.Y, area.Width, area.Height));
        }

        return result;
    }
}
