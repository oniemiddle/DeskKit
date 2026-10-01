using DeskKit.Core.Models;

namespace DeskKit.Core.Services;

/// <summary>
/// Keeps widgets reachable when the monitor layout changes.
/// <para>
/// A widget whose saved position is now off every screen — because a monitor was
/// unplugged, or the resolution shrank — is nudged back to the nearest screen
/// instead of being left stranded where it cannot be clicked.
/// </para>
/// </summary>
public static class PlacementNormalizer
{
    /// <summary>How much of the widget must stay on screen for it to be reachable.</summary>
    public const int RequiredVisibleWidth = 80;

    public const int RequiredVisibleHeight = 40;

    public static WidgetPlacement EnsureOnScreen(WidgetPlacement placement, IReadOnlyList<ScreenBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(screens);

        if (screens.Count == 0)
            return placement;

        var alreadyVisible = screens.Any(screen =>
            screen.HasVisibleCorner(placement.X, placement.Y, RequiredVisibleWidth, RequiredVisibleHeight));

        if (alreadyVisible)
            return placement;

        var target = FindNearestScreen(placement, screens);
        var x = Math.Clamp(placement.X, target.X, Math.Max(target.X, target.Right - RequiredVisibleWidth));
        var y = Math.Clamp(placement.Y, target.Y, Math.Max(target.Y, target.Bottom - RequiredVisibleHeight));

        return placement with { X = x, Y = y };
    }

    public static IReadOnlyList<WidgetPlacement> EnsureAllOnScreen(
        IReadOnlyList<WidgetPlacement> placements, IReadOnlyList<ScreenBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(placements);

        if (screens.Count == 0)
            return placements;

        var result = new List<WidgetPlacement>(placements.Count);
        foreach (var placement in placements)
            result.Add(EnsureOnScreen(placement, screens));

        return result;
    }

    /// <summary>
    /// Picks the screen whose centre is closest to the widget, so a widget that
    /// lived on the right-hand monitor does not jump to the left one.
    /// </summary>
    private static ScreenBounds FindNearestScreen(WidgetPlacement placement, IReadOnlyList<ScreenBounds> screens)
    {
        var best = screens[0];
        var bestDistance = double.MaxValue;

        foreach (var screen in screens)
        {
            var centreX = screen.X + (screen.Width / 2.0);
            var centreY = screen.Y + (screen.Height / 2.0);
            var dx = centreX - placement.X;
            var dy = centreY - placement.Y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = screen;
            }
        }

        return best;
    }
}
