namespace DeskKit.Core;

/// <summary>
/// Well known locations on disk. Everything DeskKit persists stays under the
/// user's roaming application data, so the data survives upgrades and is never
/// mixed into the installation directory.
/// </summary>
public static class AppPaths
{
    public const string AppName = "DeskKit";

    public const string ConfigFileName = "config.json";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ConfigFile => Path.Combine(DataDirectory, ConfigFileName);

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string IconCacheDirectory => Path.Combine(DataDirectory, "icon-cache");
}
