using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Widgets.Localization;

namespace DeskKit.Widgets.TickTick;

/// <summary>Describes the TickTick task widget to the shell.</summary>
public sealed class TickTickWidgetProvider : IWidgetProvider
{
    public const string WidgetId = "ticktick";

    public WidgetDescriptor Descriptor { get; } = new(
        Id: WidgetId,
        DisplayName: WidgetText.TickTickName,
        Description: WidgetText.TickTickDescription,
        DefaultWidth: 340,
        DefaultHeight: 360,
        MinWidth: 220,
        MinHeight: 180,
        PreventActivation: false);

    public WidgetViewModel Create(WidgetContext context) => new TickTickViewModel(context);
}
