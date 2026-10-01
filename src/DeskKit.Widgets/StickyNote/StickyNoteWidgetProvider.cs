using Avalonia.Controls;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;

namespace DeskKit.Widgets.StickyNote;

/// <summary>Describes the sticky note widget to the shell.</summary>
public sealed class StickyNoteWidgetProvider : IWidgetProvider
{
    public const string WidgetId = "sticky-note";

    public WidgetDescriptor Descriptor { get; } = new(
        Id: WidgetId,
        DisplayName: "便签",
        Description: "可以随手打字的桌面便签",
        DefaultWidth: 280,
        DefaultHeight: 220,
        MinWidth: 160,
        MinHeight: 120,

        // Notes need the keyboard, so this is the one widget that is allowed to
        // take focus when clicked.
        PreventActivation: false);

    public WidgetViewModel Create(WidgetContext context) => new StickyNoteViewModel(context);
}
