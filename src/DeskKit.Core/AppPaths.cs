namespace DeskKit.Core;

/// <summary>
/// Well known locations on disk. Everything DeskKit persists stays under the
/// user's application data, so the data survives upgrades and is never mixed into
/// the installation directory.
/// <para>
/// The configuration stays in the roaming folder on purpose: it holds the user's
/// own content — the text of a note, the shortcuts in a launcher — and roaming
/// profiles are the ones that get backed up and synchronised. Logs go to the local
/// folder, which is where machine-local output belongs and keeps them out of a
/// roaming profile that would otherwise carry them to a server.
/// </para>
/// </summary>
public static class AppPaths
{
    public const string AppName = "DeskKit";

    public const string ConfigFileName = "config.json";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string ConfigFile => Path.Combine(DataDirectory, ConfigFileName);

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName,
        "logs");
}
