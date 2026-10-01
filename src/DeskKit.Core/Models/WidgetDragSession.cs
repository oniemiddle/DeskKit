using Avalonia;

namespace DeskKit.Core.Models;

/// <summary>
/// The arithmetic for dragging a widget window with the pointer.
/// <para>
/// The whole point of this type is that the window position is a pure function
/// of <em>the pointer's current screen position</em> and a fixed grab offset
/// recorded when the drag began. It must never be derived from the window's
/// current position, because the window moves as a result of the calculation:
/// feeding its own position back in makes the result depend on the previous
/// frame, and the sequence
/// <c>x ← pointer − x</c> oscillates. In a real drag that shows up as the window
/// snapping back to where the drag started and then jumping forward again.
/// </para>
/// <para>
/// The grab offset is the vector from the window's origin to the point the user
/// grabbed, in physical pixels. Offsetting the live pointer position by it puts
/// the window exactly under the cursor, so the widget tracks the pointer 1:1.
/// </para>
/// </summary>
public readonly record struct WidgetDragSession
{
    private WidgetDragSession(PixelPoint grabOffset) => GrabOffset = grabOffset;

    /// <summary>
    /// Vector from the window origin to the grabbed point, in physical pixels.
    /// </summary>
    public PixelPoint GrabOffset { get; }

    /// <summary>
    /// Begins a drag with the pointer and window both in physical screen
    /// coordinates, captured at the moment the button went down.
    /// </summary>
    public static WidgetDragSession Start(PixelPoint pointerScreen, PixelPoint windowPosition) =>
        new(new PixelPoint(
            pointerScreen.X - windowPosition.X,
            pointerScreen.Y - windowPosition.Y));

    /// <summary>
    /// Where the window belongs for the given pointer position. Calling this
    /// twice with the same pointer position always gives the same answer,
    /// whatever the window did in between.
    /// </summary>
    public PixelPoint PositionFor(PixelPoint pointerScreen) =>
        new(pointerScreen.X - GrabOffset.X, pointerScreen.Y - GrabOffset.Y);
}
