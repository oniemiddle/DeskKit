using DeskKit.Core.Abstractions;
using DeskKit.Widgets.Clock;
using DeskKit.Widgets.QuickLaunch;
using DeskKit.Widgets.StickyNote;
using Microsoft.Extensions.DependencyInjection;

namespace DeskKit.Widgets;

/// <summary>
/// Dependency-injection registrations for the built-in widget catalogue.
/// <para>
/// Providers are registered here at compile time. The shell only depends on
/// <see cref="IWidgetProvider"/>, so a future plugin loader can add providers
/// from separate assemblies without any change to the shell.
/// </para>
/// </summary>
public static class WidgetServiceCollectionExtensions
{
    /// <summary>
    /// Adds DeskKit's built-in providers to the host's composition root.
    /// Extensions use the same registration seam, so the shell never needs a
    /// catalogue of concrete widget types.
    /// </summary>
    public static IServiceCollection AddBuiltInWidgets(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWidgetProvider, ClockWidgetProvider>();
        services.AddSingleton<IWidgetProvider, StickyNoteWidgetProvider>();
        services.AddSingleton<IWidgetProvider>(provider =>
            new QuickLaunchWidgetProvider(provider.GetRequiredService<IShellIconLoader>()));

        return services;
    }
}
