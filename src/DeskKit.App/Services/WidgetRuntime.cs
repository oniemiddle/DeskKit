using DeskKit.Core.Models;
using DeskKit.App.Views;

namespace DeskKit.App.Services;

/// <summary>One placed widget: its stored placement, its view model and its window.</summary>
internal sealed class WidgetRuntime(
    WidgetPlacement placement, WidgetViewModel viewModel, WidgetWindow window)
{
    public WidgetPlacement Placement { get; set; } = placement;

    public WidgetViewModel ViewModel { get; } = viewModel;

    public WidgetWindow Window { get; } = window;

    public bool IsVisible { get; set; } = true;
}

/// <summary>Public view of a placed widget, for the settings window.</summary>
public sealed record WidgetInfo(string InstanceId, string WidgetId, string DisplayName, WidgetViewModel ViewModel);
