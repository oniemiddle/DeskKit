using System.Text.Json;
using DeskKit.Core.Models;

namespace DeskKit.Core.Services;

/// <summary>What the old JSON configuration files said, if there were any.</summary>
/// <param name="Source">Which files it came from, kept so the database can record it.</param>
internal sealed record LegacyImport(
    AppState State,
    string Source,
    IReadOnlyList<StoreProblem> Problems);

/// <summary>
/// Reads the configuration the application used to keep in JSON, so an existing
/// installation keeps its layout after the move to a database.
/// </summary>
/// <remarks>
/// Read only, once: nothing here writes to, renames or deletes anything in the old
/// folder. It stays exactly as it was, which is what makes a downgrade confusing
/// rather than destructive. Three layouts are recognised, newest first: preferences
/// and one file per widget, the single file they were split out of, and the copy
/// that single file was moved aside to.
/// <para>
/// The serialization here is deliberately reflective rather than source generated.
/// It runs once, against files written by three builds over time, and tolerating
/// either casing is worth more than a build-time contract for shapes nothing writes
/// any more.
/// </para>
/// </remarks>
internal static class LegacyJsonImport
{
    /// <summary>The marker the file store put in the name of a file it could not parse.</summary>
    private const string CorruptMarker = ".corrupt-";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>True when there is anything in this folder to import.</summary>
    public static bool Exists(string directory) => FindSource(directory) is not null;

    public static LegacyImport? Read(string directory)
    {
        if (FindSource(directory) is not { } source)
            return null;

        if (source == Source.Split)
        {
            var problems = new List<StoreProblem>();
            var settingsPath = Path.Combine(directory, AppPaths.SettingsFileName);
            var widgetFiles = ListWidgetFiles(Path.Combine(directory, AppPaths.WidgetsDirectoryName));

            var state = new AppState
            {
                Settings = File.Exists(settingsPath)
                    ? ReadSettings(settingsPath, problems)
                    : new AppSettings(),
                Widgets = ReadWidgetFiles(widgetFiles, problems),
            };

            var described = widgetFiles.Count == 0
                ? AppPaths.SettingsFileName
                : $"{AppPaths.SettingsFileName} + {AppPaths.WidgetsDirectoryName}/ ({widgetFiles.Count})";

            return new LegacyImport(state, described, problems);
        }

        var name = source == Source.Config
            ? AppPaths.LegacyConfigFileName
            : AppPaths.LegacyBackupFileName;

        var singleProblems = new List<StoreProblem>();
        var read = ReadSingleFile(Path.Combine(directory, name), name, singleProblems);

        return new LegacyImport(read, name, singleProblems);
    }

    private enum Source
    {
        Split,
        Config,
        ConfigBackup,
    }

    private static Source? FindSource(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        if (File.Exists(Path.Combine(directory, AppPaths.SettingsFileName))
            || ListWidgetFiles(Path.Combine(directory, AppPaths.WidgetsDirectoryName)).Count > 0)
        {
            return Source.Split;
        }

        if (File.Exists(Path.Combine(directory, AppPaths.LegacyConfigFileName)))
            return Source.Config;

        // Last resort: the single file as it was left when it was split up. It is the
        // only copy of the layout in the state where nothing else was found.
        return File.Exists(Path.Combine(directory, AppPaths.LegacyBackupFileName))
            ? Source.ConfigBackup
            : null;
    }

    private static List<string> ListWidgetFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        var files = new List<string>();

        // "*.json" already leaves out the ".tmp" of an interrupted write.
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (Path.GetFileName(path).Contains(CorruptMarker, StringComparison.Ordinal))
                continue;

            files.Add(path);
        }

        // Directory order is not guaranteed; sorting makes the import repeatable.
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static AppSettings ReadSettings(string path, List<StoreProblem> problems)
    {
        if (!TryReadAllText(path, out var json))
        {
            problems.Add(new StoreProblem(
                AppPaths.SettingsFileName,
                "the file could not be read, so default preferences were used"));
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<LegacySettingsFile>(json, Options)?.Settings
                ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            problems.Add(new StoreProblem(
                AppPaths.SettingsFileName,
                $"the file could not be understood, so default preferences were used: {ex.Message}"));
            return new AppSettings();
        }
    }

    private static List<WidgetPlacement> ReadWidgetFiles(
        List<string> files,
        List<StoreProblem> problems)
    {
        var widgets = new List<(int Order, WidgetPlacement Placement)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in files)
        {
            // The file name was the instance id, so it cannot disagree with itself.
            var instanceId = Path.GetFileNameWithoutExtension(path);

            if (!seen.Add(instanceId))
            {
                problems.Add(new StoreProblem(instanceId, "more than one file claims this widget"));
                continue;
            }

            if (!TryReadAllText(path, out var json))
            {
                problems.Add(new StoreProblem(instanceId, "the file could not be read"));
                continue;
            }

            LegacyWidgetFile? file = null;
            try
            {
                file = JsonSerializer.Deserialize<LegacyWidgetFile>(json, Options);
            }
            catch (JsonException ex)
            {
                problems.Add(new StoreProblem(instanceId, $"the file could not be understood: {ex.Message}"));
                continue;
            }

            if (file is null)
            {
                problems.Add(new StoreProblem(instanceId, "the file held nothing"));
                continue;
            }

            widgets.Add((file.Order, new WidgetPlacement
            {
                InstanceId = instanceId,
                WidgetId = file.WidgetId,
                Enabled = file.Enabled,
                X = file.X,
                Y = file.Y,
                Width = file.Width,
                Height = file.Height,
                SettingsVersion = file.SettingsVersion,
                Settings = file.Settings,
            }));
        }

        // Order is only for a stable listing; the instance id breaks ties so two
        // files claiming the same order still come out the same way every run.
        return widgets
            .OrderBy(widget => widget.Order)
            .ThenBy(widget => widget.Placement.InstanceId, StringComparer.Ordinal)
            .Select(widget => widget.Placement)
            .ToList();
    }

    private static AppState ReadSingleFile(string path, string name, List<StoreProblem> problems)
    {
        if (!TryReadAllText(path, out var json))
        {
            problems.Add(new StoreProblem(name, "the file could not be read"));
            return new AppState();
        }

        AppState? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AppState>(json, Options);
        }
        catch (JsonException ex)
        {
            problems.Add(new StoreProblem(name, $"the file could not be understood: {ex.Message}"));
            return new AppState();
        }

        if (parsed is null)
            return new AppState();

        // The instance id is the primary key here, so the widgets that have none and
        // the ones that repeat cannot all be kept.
        var widgets = new List<WidgetPlacement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var placement in parsed.Widgets)
        {
            if (string.IsNullOrWhiteSpace(placement.InstanceId))
            {
                problems.Add(new StoreProblem(name, "a widget without an instance id was skipped"));
                continue;
            }

            if (!seen.Add(placement.InstanceId))
            {
                problems.Add(new StoreProblem(
                    placement.InstanceId,
                    "a repeated widget was skipped, the first one was kept"));
                continue;
            }

            widgets.Add(placement);
        }

        return parsed with { Widgets = widgets };
    }

    private static bool TryReadAllText(string path, out string json)
    {
        try
        {
            json = File.ReadAllText(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            json = string.Empty;
            return false;
        }
    }

    /// <summary>The shape of the preferences file the split layout used.</summary>
    private sealed record LegacySettingsFile
    {
        public int Version { get; init; }

        public AppSettings Settings { get; init; } = new();
    }

    /// <summary>The shape of one widget file the split layout used.</summary>
    private sealed record LegacyWidgetFile
    {
        public int Order { get; init; }

        public string WidgetId { get; init; } = string.Empty;

        public bool Enabled { get; init; } = true;

        public int X { get; init; }

        public int Y { get; init; }

        public double Width { get; init; }

        public double Height { get; init; }

        public int SettingsVersion { get; init; } = 1;

        public Dictionary<string, JsonElement> Settings { get; init; } = [];
    }
}
