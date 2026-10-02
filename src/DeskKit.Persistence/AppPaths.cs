namespace DeskKit.Persistence;

/// <summary>
/// Well known locations on disk. Everything DeskKit persists stays under the
/// user's application data, so the data survives upgrades and is never mixed into
/// the installation directory.
/// </summary>
/// <remarks>
/// The database lives in the <b>local</b> application data folder. It used to be a
/// set of JSON files in the roaming one, on the grounds that they hold the user's
/// own content 鈥?the text of a note, the shortcuts in a launcher 鈥?and roaming
/// profiles are the ones that get backed up and synchronised. A database cannot
/// live there: it is one file written in place, so a profile copy or a folder
/// redirection that reaches it while it is open is a way to corrupt it. The trade is
/// stated plainly in the README: user content no longer travels with a roaming
/// profile.
/// <para>
/// Logs stay in the local folder too, which is where machine-local output belongs.
/// </para>
/// <para>
/// The JSON layout is still read, once, from the roaming folder it was written to 鈥?
/// see <c>LegacyJsonImport</c>. Nothing in that folder is ever written again.
/// </para>
/// </remarks>
public static class AppPaths
{
    public const string AppName = "DeskKit";

    /// <summary>The single file holding everything the app persists.</summary>
    public const string DatabaseFileName = "deskkit.db";

    /// <summary>
    /// A whole copy of the database, taken before a schema migration runs. One fixed
    /// name rather than one per migration, because there is only ever one worth
    /// keeping and a directory full of them would help nobody.
    /// </summary>
    public const string PreMigrationBackupFileName = "deskkit.pre-migration-backup.db";

    /// <summary>The preferences file of the JSON layout this used to store.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>One file per placed widget in the JSON layout.</summary>
    public const string WidgetsDirectoryName = "widgets";

    /// <summary>The single file the whole configuration used to live in.</summary>
    public const string LegacyConfigFileName = "config.json";

    /// <summary>
    /// Where <see cref="LegacyConfigFileName"/> was put once it had been split up.
    /// Only read by the import, which has to recognise it as not being a source.
    /// </summary>
    public const string LegacyBackupFileName = "config.pre-split-backup.json";

    /// <summary>Where the database lives.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, DatabaseFileName);

    /// <summary>
    /// The roaming folder the JSON files were written to. Read once by the import,
    /// and never written to again.
    /// </summary>
    public static string LegacyDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string LegacySettingsPath { get; } =
        Path.Combine(LegacyDataDirectory, SettingsFileName);

    public static string LegacyWidgetsDirectory { get; } =
        Path.Combine(LegacyDataDirectory, WidgetsDirectoryName);

    public static string LegacyConfigPath { get; } =
        Path.Combine(LegacyDataDirectory, LegacyConfigFileName);

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");
}
