using DeskKit.Core.Abstractions;
using DeskKit.Widgets.Clock;
using DeskKit.Widgets.QuickLaunch;
using DeskKit.Widgets.StickyNote;

namespace DeskKit.Widgets;

/// <summary>
/// The built-in widget catalogue.
/// <para>
/// Providers are registered here at compile time. The shell only depends on
/// <see cref="IWidgetProvider"/>, so a future plugin loader can add providers
/// from separate assemblies without any change to the shell.
/// </para>
/// </summary>
public static class BuiltInWidgets
{
    public static IReadOnlyList<IWidgetProvider> CreateProviders(IShellIconLoader iconLoader) =>
    [
        new ClockWidgetProvider(),
        new StickyNoteWidgetProvider(),
        new QuickLaunchWidgetProvider(iconLoader),
    ];
}
