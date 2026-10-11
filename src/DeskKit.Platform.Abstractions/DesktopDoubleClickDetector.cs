namespace DeskKit.Platform;

/// <summary>One left-button press, as the desktop gesture watcher saw it.</summary>
/// <param name="X">Screen coordinate, physical pixels.</param>
/// <param name="Y">Screen coordinate, physical pixels.</param>
/// <param name="TimeMs">The timestamp the input carried.</param>
/// <param name="IsDesktopBackdrop">Whether the press landed on the wallpaper, off every icon and window.</param>
public readonly record struct PointerSample(int X, int Y, uint TimeMs, bool IsDesktopBackdrop);

/// <summary>
/// Reconstructs a double-click on the desktop backdrop from pointer presses.
/// </summary>
/// <remarks>
/// A low-level mouse hook reports raw button presses and no click count, so the
/// count Windows would have kept has to be kept here. The rule reproduced is
/// Windows' own: two presses on the backdrop, no further apart in time than a
/// double-click is allowed to be and no further apart in space than the system's
/// double-click rectangle. The timings are injected rather than read from the
/// machine, so every branch is pinned by a unit test instead of being changed by
/// how one desktop happens to be configured.
/// <para>
/// Instances are not thread-safe; the watcher drives one from a single thread.
/// </para>
/// </remarks>
public sealed class DesktopDoubleClickDetector(
    uint doubleClickTimeMs,
    int maxDistanceX,
    int maxDistanceY)
{
    private PointerSample _lastPress;
    private bool _hasLastPress;

    /// <summary>
    /// True when this press completed a double-click on the backdrop. A press
    /// anywhere else ends the sequence, so a click on a widget or on an icon
    /// cannot be half of one.
    /// </summary>
    public bool Accept(in PointerSample sample)
    {
        if (!sample.IsDesktopBackdrop)
        {
            _hasLastPress = false;
            return false;
        }

        if (!_hasLastPress)
        {
            Remember(sample);
            return false;
        }

        if (IsWithinDoubleClickWindow(sample))
        {
            // Consumed: a third press starts a new pair rather than finishing this
            // one twice, which is what Windows does with a triple click.
            _hasLastPress = false;
            return true;
        }

        Remember(sample);
        return false;
    }

    private void Remember(in PointerSample sample)
    {
        _lastPress = sample;
        _hasLastPress = true;
    }

    private bool IsWithinDoubleClickWindow(in PointerSample sample)
    {
        // A timestamp that went backwards is not a double-click: the system clock
        // can jump, and treating a jump as a match would fire the gesture at a
        // random moment.
        if (sample.TimeMs < _lastPress.TimeMs)
            return false;

        if (sample.TimeMs - _lastPress.TimeMs > doubleClickTimeMs)
            return false;

        return Math.Abs(sample.X - _lastPress.X) <= maxDistanceX
            && Math.Abs(sample.Y - _lastPress.Y) <= maxDistanceY;
    }
}
