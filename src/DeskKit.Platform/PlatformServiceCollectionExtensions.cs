using DeskKit.Core.Abstractions;
using DeskKit.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DeskKit.Platform;

/// <summary>
/// Registers the platform capabilities used by the application shell.
/// </summary>
/// <remarks>
/// The application deliberately calls one cross-platform composition method. OS
/// selection stays beside the capability implementations, so adding macOS or
/// Linux services does not leak conditional platform code into the shell.
/// </remarks>
public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddDeskKitPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDesktopLayerService>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsDesktopLayerService()
                : new NullDesktopLayerService());

        services.AddSingleton<IWindowMaterialService>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsWindowMaterialService()
                : new NullWindowMaterialService());

        services.AddSingleton<IAutoStartService>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsAutoStartService()
                : new NullAutoStartService());

        services.AddSingleton<IShellIconLoader>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsShellIconLoader()
                : new NullShellIconLoader());

        return services;
    }
}
