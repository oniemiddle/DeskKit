namespace DeskKit.Core.Models;

/// <summary>
/// Converts between a widget window's outer size and the size of the visible card
/// inside it.
/// <para>
/// A window keeps a transparent inset around the card so the card's own drop shadow
/// has somewhere to render; when the platform supplies a surface material the inset
/// is zero and the window and the card are the same rectangle. Sizes are logical
/// pixels, because that is the unit the layout system works in.
/// </para>
/// <para>
/// Pure, so the arithmetic is unit-tested rather than discovered on screen.
/// </para>
/// </summary>
public static class WindowCardGeometry
{
    /// <summary>The window size that holds a card of the given size.</summary>
    public static (double Width, double Height) WindowSizeForCard(
        double cardWidth, double cardHeight, double margin) =>
        (cardWidth + (margin * 2), cardHeight + (margin * 2));

    /// <summary>
    /// The card size held by a window of the given size. Never smaller than one
    /// pixel, so a window that has not been laid out yet cannot produce a card of
    /// zero or negative size.
    /// </summary>
    public static (double Width, double Height) CardSizeForWindow(
        double windowWidth, double windowHeight, double margin) =>
        (Math.Max(1, windowWidth - (margin * 2)), Math.Max(1, windowHeight - (margin * 2)));
}
