using Avalonia;
using DeskKit.Runtime.Views;
using DeskKit.Core.Models;
using DeskKit.Runtime.Views;

namespace DeskKit.Runtime;

/// <summary>One placed widget: its stored placement, its view model and its window.</summary>
internal sealed class WidgetRuntime(
    WidgetPlacement placement, WidgetViewModel viewModel, WidgetWindow window) : IPlaceableWidget
{
    public WidgetPlacement Placement { get; set; } = placement;

    public WidgetViewModel ViewModel { get; } = viewModel;

    public WidgetWindow Window { get; } = window;

    public bool IsVisible { get; set; } = true;

    PixelPoint IPlaceableWidget.Position => Window.Position;

    bool IPlaceableWidget.TryGetCardSize(out PixelSize size, out double scaling)
    {
        size = default;

        var card = Window.CardBounds;
        if (card.Width <= 0 || card.Height <= 0)
        {
            scaling = 1;
            return false;
        }

        scaling = Window.RenderScaling > 0 ? Window.RenderScaling : 1;
        size = new PixelSize(
            (int)Math.Round(card.Width * scaling),
            (int)Math.Round(card.Height * scaling));

        return true;
    }

    void IPlaceableWidget.SetSnapHighlight(IReadOnlyList<WidgetGlowSegment> segments) =>
        Window.SetSnapHighlight(segments);
}

/// <summary>Public view of a placed widget, for the settings window.</summary>
public sealed record WidgetInfo(string InstanceId, string WidgetId, string DisplayName, WidgetViewModel ViewModel);
