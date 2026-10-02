using Avalonia;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core.Models;

namespace DeskKit.App.Shell;

/// <summary>
/// One widget as the placement rules see it: something that can be measured and lit up,
/// without the rules having to know it owns a window.
/// </summary>
/// <remarks>
/// The seam exists so the snapping arithmetic can be exercised against rectangles
/// instead of real windows. Everything here is in physical pixels, because that is the
/// unit the pointer and the window position are both in.
/// </remarks>
internal interface IPlaceableWidget
{
    bool IsVisible { get; }

    /// <summary>Where the window sits, in physical screen pixels.</summary>
    PixelPoint Position { get; }

    /// <summary>
    /// The visible card's size in physical pixels, with the window's render scaling so
    /// a caller can convert the surface margin into the same unit. False before the
    /// window has been laid out, when there is nothing meaningful to measure.
    /// </summary>
    bool TryGetCardSize(out PixelSize size, out double scaling);

    void SetSnapHighlight(IReadOnlyList<WidgetGlowSegment> segments);
}

/// <summary>
/// Magnetic snapping, and copying a widget's live position and size back into what is
/// stored.
/// </summary>
/// <remarks>
/// Rectangles are compared in <b>card</b> space, not window space: a window carries a
/// transparent margin so the card's glow has somewhere to render, and measuring the
/// window instead would make two snapped widgets sit twice the margin further apart
/// than the configured gap.
/// <para>
/// Nothing here owns a widget: the list is passed in, so this holds no state and can be
/// exercised against stand-in rectangles.
/// </para>
/// </remarks>
internal sealed class PlacementController(WorkspaceState workspace)
{
    /// <summary>
    /// Applies magnetism to a proposed drag position and highlights whatever the widget
    /// snapped to, including the widget being dragged so the magnetised group reads as
    /// one.
    /// </summary>
    public PixelPoint Snap(
        IReadOnlyList<IPlaceableWidget> widgets,
        IPlaceableWidget moving,
        PixelPoint proposed,
        double surfaceMargin)
    {
        ArgumentNullException.ThrowIfNull(widgets);
        ArgumentNullException.ThrowIfNull(moving);

        if (!moving.TryGetCardSize(out var cardSize, out var movingScaling))
        {
            ClearHighlights(widgets);
            return proposed;
        }

        // The card sits inside the window by the surface margin, so the same offset has
        // to come off every rectangle before comparing and go back on afterwards.
        var offset = SurfaceOffset(surfaceMargin, movingScaling);

        var candidates = new List<PixelRect>(widgets.Count);
        var owners = new List<IPlaceableWidget>(widgets.Count);

        foreach (var other in widgets)
        {
            if (ReferenceEquals(other, moving) || !other.IsVisible)
                continue;

            if (!other.TryGetCardSize(out var otherSize, out var otherScaling))
                continue;

            candidates.Add(new PixelRect(
                other.Position + SurfaceOffset(surfaceMargin, otherScaling), otherSize));
            owners.Add(other);
        }

        if (candidates.Count == 0)
        {
            ClearHighlights(widgets);
            return proposed;
        }

        var proposedCard = new PixelRect(proposed + offset, cardSize);
        var result = WidgetSnapEngine.Snap(proposedCard, candidates);

        ClearHighlights(widgets);

        if (result.Snapped)
        {
            // Each side lights the stretch it actually shares with the other, so the glow
            // points at the specific region the two widgets have in common rather than
            // vaguely at a whole edge.
            moving.SetSnapHighlight(result.Glow);

            var settled = new PixelRect(result.Position, cardSize);

            foreach (var index in result.Neighbours)
            {
                if (index < 0 || index >= owners.Count)
                    continue;

                owners[index].SetSnapHighlight(
                    WidgetSnapEngine.GlowSegments(candidates[index], [settled]));
            }
        }

        return result.Position - offset;
    }

    public static void ClearHighlights(IReadOnlyList<IPlaceableWidget> widgets)
    {
        ArgumentNullException.ThrowIfNull(widgets);

        foreach (var widget in widgets)
            widget.SetSnapHighlight([]);
    }

    /// <summary>
    /// Copies the window's live position and size back into the stored placement.
    /// Position is physical, size is logical; see <see cref="WidgetPlacement"/> for why.
    /// </summary>
    public void Capture(WidgetRuntime runtime, double surfaceMargin)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var window = runtime.Window;

        // Stored sizes describe the visible card, not the window, so the transparent
        // margin never leaks into the persisted layout.
        var card = WidgetWindow.CardSizeForWindow(
            window.Width > 0 ? window.Width : window.Bounds.Width,
            window.Height > 0 ? window.Height : window.Bounds.Height,
            surfaceMargin);

        runtime.Placement = runtime.Placement with
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = card.Width,
            Height = card.Height,
        };

        workspace.SetPlacement(runtime.Placement);
    }

    private static PixelVector SurfaceOffset(double surfaceMargin, double scaling)
    {
        var offset = (int)Math.Round(surfaceMargin * scaling);
        return new PixelVector(offset, offset);
    }
}
