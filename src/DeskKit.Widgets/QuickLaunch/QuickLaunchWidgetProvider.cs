using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Widgets.Localization;

namespace DeskKit.Widgets.QuickLaunch;

/// <summary>Describes the quick launcher widget to the shell.</summary>
public sealed class QuickLaunchWidgetProvider(IShellIconLoader iconLoader)
    : IWidgetProvider, IWidgetSettingsMigrations
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

    /// <summary>
    /// 2 writes the shortcut list with the configuration's own naming. 1 left it in
    /// the serializer's default, so a launcher set up by that build holds "Name" and
    /// "Target" keys inside an otherwise camelCase file.
    /// </summary>
    public int SettingsVersion => 2;

    public IReadOnlyList<WidgetSettingsMigration> Migrations { get; } =
    [
        new(1, settings =>
        {
            // Reading is case-insensitive, so this is the list version 1 wrote.
            // Writing it back serialises it the way everything around it is written.
            var items = settings.Get(
                QuickLaunchViewModel.KeyItems,
                new List<QuickLaunchItem>());

            if (items.Count > 0)
                settings.Set(QuickLaunchViewModel.KeyItems, items);
        }),
    ];

    public WidgetViewModel Create(WidgetContext context) => new QuickLaunchViewModel(context, _iconLoader);
}
