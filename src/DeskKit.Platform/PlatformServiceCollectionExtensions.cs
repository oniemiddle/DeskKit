using DeskKit.Core.Abstractions;
#if WINDOWS
using DeskKit.Platform.Windows;
#endif
using Microsoft.Extensions.DependencyInjection;

namespace DeskKit.Platform;

/// <summary>
/// Registers the platform capabilities used by the application shell.
/// </summary>
/// <remarks>
/// The application deliberately calls one composition method and never tests the
/// operating system itself. The choice is made when this assembly is compiled rather
/// than when it runs: the Windows target framework references
/// <c>DeskKit.Platform.Windows</c> and registers the implementations it holds, and a
/// target framework that has no implementation to reference registers the no-op
/// capabilities instead.
/// </remarks>
public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddDeskKitPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

#if WINDOWS
        services.AddSingleton<IDesktopLayerService, WindowsDesktopLayerService>();
        services.AddSingleton<IWindowMaterialService, WindowsWindowMaterialService>();
        services.AddSingleton<IDesktopGestureService, WindowsDesktopGestureService>();
        services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        services.AddSingleton<IShellIconLoader, WindowsShellIconLoader>();
        services.AddSingleton<INotificationWindowStyler, WindowsNotificationWindowStyler>();
#else
        services.AddSingleton<IDesktopLayerService, NullDesktopLayerService>();
        services.AddSingleton<IWindowMaterialService, NullWindowMaterialService>();
        services.AddSingleton<IDesktopGestureService, NullDesktopGestureService>();
        services.AddSingleton<IAutoStartService, NullAutoStartService>();
        services.AddSingleton<IShellIconLoader, NullShellIconLoader>();
        services.AddSingleton<INotificationWindowStyler>(NullNotificationWindowStyler.Instance);
#endif

        return services;
    }
}
