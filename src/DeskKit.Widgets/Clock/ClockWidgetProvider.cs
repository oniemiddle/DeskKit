using Avalonia.Controls;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;

namespace DeskKit.Widgets.Clock;

/// <summary>Describes the clock widget to the shell.</summary>
public sealed class ClockWidgetProvider : IWidgetProvider
{
    public const string WidgetId = "clock";

    public WidgetDescriptor Descriptor { get; } = new(
        Id: WidgetId,
        DisplayName: "时钟",
        Description: "显示时间和日期",
        DefaultWidth: 260,
        DefaultHeight: 130,
        MinWidth: 140,
        MinHeight: 80,

        // A clock is decoration: clicking it must not pull focus away from
        // whatever the user is doing.
        PreventActivation: true);

    public WidgetViewModel Create(WidgetContext context) => new ClockViewModel(context);
}
