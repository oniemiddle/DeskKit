using System.Text.Json;
using DeskKit.Core.Models;
using DeskKit.Core.Serialization;

namespace DeskKit.Core.Services;

/// <summary>
/// Loads and stores <see cref="AppState"/>.
/// <para>
/// Writes go to a temporary file that is then swapped into place, so a crash or
/// power loss mid-write can never leave a half-written configuration behind. A
/// file that cannot be parsed is moved aside rather than deleted, so a user can
/// recover hand-edited settings.
/// </para>
/// </summary>
public sealed class ConfigStore
{
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
            return new AppState();

        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (IOException)
        {
            return new AppState();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppState();
        }

        if (string.IsNullOrWhiteSpace(json))
            return new AppState();

        try
        {
            var state = JsonSerializer.Deserialize(json, AppStateJsonContext.Default.AppState);
            return state is null ? new AppState() : Migrate(state);
        }
        catch (JsonException)
        {
            LastCorruptFileBackup = MoveAsideCorruptFile();
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

    public void Save(AppState state)
    {
        Directory.CreateDirectory(_directory);

        var json = JsonSerializer.Serialize(state, AppStateJsonContext.Default.AppState);

        // Write to a sibling file first, then swap, so the real file is never
        // observed in a partially written state.
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _filePath, overwrite: true);
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
