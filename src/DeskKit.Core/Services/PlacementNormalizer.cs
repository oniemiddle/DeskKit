using DeskKit.Core.Models;

namespace DeskKit.Core.Services;

/// <summary>
/// Keeps widgets reachable when the monitor layout changes.
/// <para>
/// A widget whose saved position is now off every screen — because a monitor was
/// unplugged, or the resolution shrank — is nudged back to the nearest screen
/// instead of being left stranded where it cannot be clicked.
/// </para>
/// <para>
/// This adjusts where a widget is <em>put</em>, not where it is stored. The caller
/// keeps the saved placement, so a monitor that comes back finds the widget where
/// it was left rather than where an unrelated display layout pushed it. Folding the
/// result back into the saved layout is what used to make a position degrade a
/// little on every undock.
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

    /// <summary>
    /// Picks the screen whose centre is closest to the widget, so a widget that
    /// lived on the right-hand monitor does not jump to the left one.
    /// </summary>
    private static ScreenBounds FindNearestScreen(WidgetPlacement placement, IReadOnlyList<ScreenBounds> screens) =>
        FindNearestScreen(placement.X, placement.Y, screens);

    /// <summary>
    /// The screen a point belongs to, or the one nearest to it when it belongs to
    /// none. The monitor a widget is on decides which edge it slides off, so the
    /// answer has to be the same rule placement uses.
    /// </summary>
    public static ScreenBounds FindNearestScreen(int x, int y, IReadOnlyList<ScreenBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentOutOfRangeException.ThrowIfZero(screens.Count);

        var best = screens[0];
        var bestDistance = double.MaxValue;

        foreach (var screen in screens)
        {
            var centreX = screen.X + (screen.Width / 2.0);
            var centreY = screen.Y + (screen.Height / 2.0);
            var dx = centreX - x;
            var dy = centreY - y;
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
