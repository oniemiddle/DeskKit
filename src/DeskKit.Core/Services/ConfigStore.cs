using System.Text.Json;
using DeskKit.Core.Models;
using DeskKit.Core.Serialization;

namespace DeskKit.Core.Services;

/// <summary>What happened the last time the configuration was loaded.</summary>
public enum ConfigLoadOutcome
{
    /// <summary>Nothing has been loaded yet.</summary>
    NotLoaded,

    /// <summary>There was no file, which is a first run rather than a problem.</summary>
    FirstRun,

    /// <summary>The file was read and understood.</summary>
    Loaded,

    /// <summary>
    /// The file is there but could not be read. Whatever it holds is unknown, so
    /// it must not be written over — see <see cref="ConfigStore.Save"/>.
    /// </summary>
    Unreadable,

    /// <summary>
    /// The file was read but could not be parsed. It has been moved aside, so the
    /// path is clear for a fresh one.
    /// </summary>
    Corrupt,
}

/// <summary>What happened when the configuration was saved.</summary>
public enum ConfigSaveOutcome
{
    Saved,

    /// <summary>
    /// Nothing was written, because the file could not be read when it was loaded.
    /// Overwriting it would destroy settings that were never seen.
    /// </summary>
    RefusedUnreadable,
}

/// <summary>
/// Loads and stores <see cref="AppState"/>.
/// <para>
/// Writes go to a temporary file that is then swapped into place, so a crash or
/// power loss mid-write can never leave a half-written configuration behind. A
/// file that cannot be parsed is moved aside rather than deleted, so a user can
/// recover hand-edited settings.
/// </para>
/// <para>
/// A file that cannot be <em>read</em> is a different matter, and the one that
/// matters most: the settings in it are unknown, so this type refuses to write
/// afterwards and leaves it alone. Losing the changes made in one session is
/// recoverable; overwriting a layout that was never read is not.
/// </para>
/// </summary>
public sealed class ConfigStore
{
    /// <summary>
    /// How many times reading is attempted before it counts as a failure. An
    /// exclusive lock is usually an antivirus scan or a backup tool holding the
    /// file for a few milliseconds, so a retry turns the common case back into a
    /// normal startup.
    /// </summary>
    public const int ReadAttempts = 3;

    /// <summary>Delay between read attempts, in milliseconds.</summary>
    public const int ReadRetryDelayMs = 60;

    private readonly string _directory;
    private readonly string _filePath;

    public ConfigStore(string? directory = null)
    {
        _directory = directory ?? AppPaths.DataDirectory;
        _filePath = Path.Combine(_directory, AppPaths.ConfigFileName);
    }

    public string DirectoryPath => _directory;

    public string FilePath => _filePath;

    /// <summary>Raised with the path of a configuration file that could not be read.</summary>
    public string? LastCorruptFileBackup { get; private set; }

    /// <summary>What the last <see cref="Load"/> found.</summary>
    public ConfigLoadOutcome LastLoadOutcome { get; private set; } = ConfigLoadOutcome.NotLoaded;

    /// <summary>
    /// True when a configuration file was present the last time
    /// <see cref="Load"/> ran. Used to tell a first run apart from a user who
    /// deliberately removed every widget.
    /// </summary>
    public bool FileExistedOnLoad { get; private set; }

    public AppState Load()
    {
        LastCorruptFileBackup = null;
        FileExistedOnLoad = File.Exists(_filePath);

        if (!FileExistedOnLoad)
        {
            LastLoadOutcome = ConfigLoadOutcome.FirstRun;
            return new AppState();
        }

        if (!TryReadAllText(out var json))
        {
            // The file is there and could not be read. Defaults are the only thing
            // this session can start from, but the file itself stays untouched:
            // Save refuses until a load succeeds.
            LastLoadOutcome = ConfigLoadOutcome.Unreadable;
            return new AppState();
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            // A file with nothing in it, which is not the same as one that could
            // not be read: there are no settings in it to protect.
            LastLoadOutcome = ConfigLoadOutcome.Loaded;
            return new AppState();
        }

        try
        {
            var state = JsonSerializer.Deserialize(json, AppStateJsonContext.Default.AppState);

            LastLoadOutcome = ConfigLoadOutcome.Loaded;
            return state is null ? new AppState() : Migrate(state);
        }
        catch (JsonException)
        {
            LastCorruptFileBackup = MoveAsideCorruptFile();
            LastLoadOutcome = ConfigLoadOutcome.Corrupt;
            return new AppState();
        }
    }

    /// <summary>
    /// Brings an older file forward. Unknown or future versions are used as-is
    /// rather than discarded, because losing the user's layout is worse than
    /// reading a field that a newer build wrote.
    /// </summary>
    public static AppState Migrate(AppState state) =>
        state.Version == AppState.CurrentVersion
            ? state
            : state with { Version = AppState.CurrentVersion };

    /// <summary>
    /// Writes the state, unless the last load failed to read a file that is still
    /// there.
    /// </summary>
    /// <param name="state">The state to persist.</param>
    /// <param name="overwriteUnreadable">
    /// Set to write even after an unreadable load. Only for a caller that means to
    /// discard what it could not read — a "reset everything" action — never for a
    /// routine save.
    /// </param>
    public ConfigSaveOutcome Save(AppState state, bool overwriteUnreadable = false)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (LastLoadOutcome == ConfigLoadOutcome.Unreadable && !overwriteUnreadable)
            return ConfigSaveOutcome.RefusedUnreadable;

        Directory.CreateDirectory(_directory);

        var json = JsonSerializer.Serialize(state, AppStateJsonContext.Default.AppState);

        // Write to a sibling file first, then swap, so the real file is never
        // observed in a partially written state.
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _filePath, overwrite: true);

        return ConfigSaveOutcome.Saved;
    }

    private bool TryReadAllText(out string json)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                json = File.ReadAllText(_filePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= ReadAttempts)
                {
                    json = string.Empty;
                    return false;
                }

                Thread.Sleep(ReadRetryDelayMs);
            }
        }
    }

    private string MoveAsideCorruptFile()
    {
        var backup = Path.Combine(
            _directory,
            $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        try
        {
            File.Move(_filePath, backup, overwrite: true);
            return backup;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
