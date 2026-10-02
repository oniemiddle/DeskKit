using Avalonia.Controls;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Widgets.Localization;

namespace DeskKit.Widgets.QuickLaunch;

/// <summary>Describes the quick launcher widget to the shell.</summary>
public sealed class QuickLaunchWidgetProvider(IShellIconLoader iconLoader) : IWidgetProvider
{
    public const string WidgetId = "quick-launch";

    private readonly IShellIconLoader _iconLoader = iconLoader;

    public WidgetDescriptor Descriptor { get; } = new(
        Id: WidgetId,
        DisplayName: WidgetText.QuickLaunchName,
        Description: WidgetText.QuickLaunchDescription,
        DefaultWidth: 300,
        DefaultHeight: 150,
        MinWidth: 140,
        MinHeight: 100,

        // Clicking a shortcut must not steal focus from the current window.
        PreventActivation: true);

    public WidgetViewModel Create(WidgetContext context) => new QuickLaunchViewModel(context, _iconLoader);
}
