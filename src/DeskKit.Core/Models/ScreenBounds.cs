namespace DeskKit.Core.Models;

/// <summary>
/// A monitor's bounds in physical pixels, expressed in the virtual desktop
/// coordinate space. Kept independent of the UI framework so placement rules
/// stay unit testable.
/// </summary>
public readonly record struct ScreenBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// True when at least <paramref name="requiredWidth"/> by
    /// <paramref name="requiredHeight"/> of the rectangle starting at
    /// (<paramref name="x"/>, <paramref name="y"/>) is inside this screen.
    /// </summary>
    public bool HasVisibleCorner(int x, int y, int requiredWidth, int requiredHeight)
    {
        if (IsEmpty)
            return false;

        var visibleWidth = Math.Min(x + requiredWidth, Right) - Math.Max(x, X);
        var visibleHeight = Math.Min(y + requiredHeight, Bottom) - Math.Max(y, Y);
        return visibleWidth >= requiredWidth && visibleHeight >= requiredHeight;
    }
}
