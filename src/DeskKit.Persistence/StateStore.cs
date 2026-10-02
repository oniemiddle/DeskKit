using System.Text.Json;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Persistence.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DeskKit.Persistence;

/// <summary>
/// Loads and stores <see cref="AppState"/> in the application's database.
/// </summary>
/// <remarks>
/// The database is the only place the application keeps state. It replaced a
/// preferences file plus one file per widget, which cost a hand-written atomic
/// write, a version number per file, a rule for every way a file could go wrong, and
/// a migration pipeline with no tooling behind it. Those are now SQLite's commit
/// protocol and EF Core's migrations.
/// <para>
/// What was traded away is worth stating: the state is no longer a text file anyone
/// can read or patch, one damaged widget no longer costs only that widget, and user
/// content no longer travels with a roaming profile. See the README.
/// </para>
/// <para>
/// Two rules survive the change. A database written by a newer build is read no
/// further and never written over. A database that cannot be opened at all is not
/// written to either, because overwriting it would destroy state that was never
/// seen; the session runs with nothing loaded and saves nothing, and says so.
/// </para>
/// </remarks>
public sealed class StateStore : IStateStore
{
    /// <summary>Where the source of an import is recorded, for the log and for support.</summary>
    private const string ImportedFromKey = "imported-from";

    private static readonly JsonSerializerOptions SettingsJson = new();

    private readonly string _databasePath;
    private readonly string _legacyDirectory;
    private readonly DbContextOptions<DeskKitDbContext> _options;

    /// <param name="databasePath">For a test, or for a second copy of the app's data.</param>
    /// <param name="legacyDirectory">Where the JSON configuration would be, if any.</param>
    public StateStore(string? databasePath = null, string? legacyDirectory = null)
    {
        _databasePath = databasePath ?? AppPaths.DatabasePath;
        _legacyDirectory = legacyDirectory ?? AppPaths.LegacyDataDirectory;
        _options = DatabaseOptions.For(_databasePath);
    }

    public string DatabasePath => _databasePath;

    /// <summary>
    /// A whole copy of the database taken before a schema migration runs. Beside the
    /// database rather than at its default location, so a test or a second data
    /// directory backs up its own database and nobody else's.
    /// </summary>
    public string PreMigrationBackupPath =>
        Path.Combine(Path.GetDirectoryName(_databasePath) ?? string.Empty, AppPaths.PreMigrationBackupFileName);

    /// <summary>What the last <see cref="Load"/> found.</summary>
    public StoreLoadReport LoadReport { get; private set; } = new(StoreOutcome.NotLoaded, [], null);

    /// <summary>
    /// True when the store found state to load —a database, or the old configuration
    /// files it imported. Used to tell a first run apart from a user who deliberately
    /// removed every widget, so that a default widget is seeded exactly once.
    /// </summary>
    public bool HasStoredState { get; private set; }

    public AppState Load()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        HasStoredState = File.Exists(_databasePath) || LegacyJsonImport.Exists(_legacyDirectory);

        if (EnsureSchema() is { } refusal)
        {
            LoadReport = refusal;
            return new AppState();
        }

        try
        {
            using var context = new DeskKitDbContext(_options);

            // A database with no preferences row in it is one this build has never
            // saved to: either it was created just now, or a run was interrupted
            // between creating it and writing the old configuration into it. Either
            // way nothing has been imported yet, so the import is attempted here
            // rather than only on the run that happened to create the file.
            if (!context.Settings.Any() && !context.Widgets.Any())
            {
                if (LegacyJsonImport.Read(_legacyDirectory) is { } import)
                {
                    Import(context, import);
                    LoadReport = new StoreLoadReport(StoreOutcome.Imported, import.Problems, import.Source);
                    return import.State;
                }

                LoadReport = new StoreLoadReport(StoreOutcome.FirstRun, [], null);
                return new AppState();
            }

            var (state, problems) = Read(context);
            LoadReport = new StoreLoadReport(StoreOutcome.Loaded, problems, null);
            return state;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            LoadReport = Unavailable(ex);
            return new AppState();
        }
    }

    /// <summary>
    /// Writes the state, unless the session may not write —see the remarks on the
    /// class. A failure to write throws, so the caller logs the reason.
    /// </summary>
    public StoreSaveReport Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        switch (LoadReport.Outcome)
        {
            case StoreOutcome.NewerSchema:
                return new StoreSaveReport(StoreSaveOutcome.RefusedNewerSchema, 0);

            case StoreOutcome.Unavailable:
                return new StoreSaveReport(StoreSaveOutcome.RefusedUnavailable, 0);
        }

        using var context = new DeskKitDbContext(_options);
        Write(context, state);

        return new StoreSaveReport(StoreSaveOutcome.Saved, context.SaveChanges());
    }

    // ---- Schema ----------------------------------------------------------

    /// <summary>
    /// Creates the database if it is missing and brings its schema up to this build.
    /// </summary>
    /// <returns>A report when the database may not be used at all, otherwise null.</returns>
    private StoreLoadReport? EnsureSchema()
    {
        try
        {
            using var context = new DeskKitDbContext(_options);

            var known = context.Database.GetMigrations().ToList();
            var applied = context.Database.GetAppliedMigrations().ToList();

            // A migration this build has never heard of means the file was written by
            // a newer build. Migrating it here would apply this build's older shape on
            // top of a newer one, and writing to it would tell the newer build that
            // data it has already moved past had not moved.
            if (applied.Except(known, StringComparer.Ordinal).Any())
            {
                return new StoreLoadReport(
                    StoreOutcome.NewerSchema,
                    [new StoreProblem(_databasePath, "the schema was written by a newer version of DeskKit")],
                    null);
            }

            if (!context.Database.GetPendingMigrations().Any())
                return null;

            // Taken before the schema changes, and only for a database that already
            // holds something: a file created moments ago has nothing to lose, and
            // copying it would only replace a good backup with an empty one.
            if (applied.Count > 0)
                DatabaseBackup.Write(_databasePath, PreMigrationBackupPath);

            context.Database.Migrate();
            return null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            return Unavailable(ex);
        }
    }

    // ---- Reading and writing --------------------------------------------

    private static (AppState State, List<StoreProblem> Problems) Read(DeskKitDbContext context)
    {
        var problems = new List<StoreProblem>();

        var settings = context.Settings
            .AsNoTracking()
            .FirstOrDefault(row => row.Id == SettingsEntity.SingletonId);

        var widgets = new List<WidgetPlacement>();

        var rows = context.Widgets
            .AsNoTracking()
            .OrderBy(row => row.Order)
            .ThenBy(row => row.InstanceId)
            .ToList();

        foreach (var row in rows)
        {
            widgets.Add(new WidgetPlacement
            {
                InstanceId = row.InstanceId,
                WidgetId = row.WidgetId,
                Enabled = row.Enabled,
                X = row.X,
                Y = row.Y,
                Width = row.Width,
                Height = row.Height,
                SettingsVersion = row.SettingsVersion,
                Settings = ReadSettings(row, problems),
            });
        }

        return (new AppState
        {
            Settings = settings is null
                ? new AppSettings()
                : new AppSettings
                {
                    Theme = settings.Theme,
                    Language = settings.Language,
                    StartWithWindows = settings.StartWithWindows,
                    ShowTrayIcon = settings.ShowTrayIcon,
                    WidgetsVisible = settings.WidgetsVisible,
                },
            Widgets = widgets,
        }, problems);
    }

    private static Dictionary<string, JsonElement> ReadSettings(
        WidgetEntity row,
        List<StoreProblem> problems)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.SettingsJson, SettingsJson)
                ?? [];
        }
        catch (JsonException ex)
        {
            // This widget's settings and not the whole layout: it comes up with its
            // defaults rather than not coming up at all.
            problems.Add(new StoreProblem(
                row.InstanceId,
                $"the stored settings could not be read, so the widget's defaults were used: {ex.Message}"));

            return [];
        }
    }

    /// <summary>
    /// Applies the state to the tracked rows. Assigning a value that is already there
    /// is what lets the caller's save report say nothing was written.
    /// </summary>
    private static void Write(DeskKitDbContext context, AppState state)
    {
        var settings = context.Settings.Find(SettingsEntity.SingletonId);
        if (settings is null)
        {
            settings = new SettingsEntity();
            context.Settings.Add(settings);
        }

        settings.Theme = state.Settings.Theme;
        settings.Language = state.Settings.Language;
        settings.StartWithWindows = state.Settings.StartWithWindows;
        settings.ShowTrayIcon = state.Settings.ShowTrayIcon;
        settings.WidgetsVisible = state.Settings.WidgetsVisible;

        var rows = context.Widgets.ToDictionary(row => row.InstanceId, StringComparer.Ordinal);
        var kept = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < state.Widgets.Count; index++)
        {
            var placement = state.Widgets[index];

            if (!rows.TryGetValue(placement.InstanceId, out var row))
            {
                row = new WidgetEntity { InstanceId = placement.InstanceId };
                context.Widgets.Add(row);
            }

            row.Order = index;
            row.WidgetId = placement.WidgetId;
            row.Enabled = placement.Enabled;
            row.X = placement.X;
            row.Y = placement.Y;
            row.Width = placement.Width;
            row.Height = placement.Height;
            row.SettingsVersion = placement.SettingsVersion;
            row.SettingsJson = JsonSerializer.Serialize(placement.Settings, SettingsJson);

            kept.Add(placement.InstanceId);
        }

        // A widget the user removed: its row goes with it, and so do the settings
        // only that widget knew about.
        foreach (var (instanceId, row) in rows)
        {
            if (!kept.Contains(instanceId))
                context.Widgets.Remove(row);
        }
    }

    private static void Import(DeskKitDbContext context, LegacyImport import)
    {
        Write(context, import.State);

        context.Meta.Add(new MetaEntity
        {
            Key = ImportedFromKey,
            Value = $"{import.Source}, {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
        });

        context.SaveChanges();
    }

    // ---- Helpers ---------------------------------------------------------

    private StoreLoadReport Unavailable(Exception ex) =>
        new(StoreOutcome.Unavailable, [new StoreProblem(_databasePath, ex.Message)], null);

    /// <summary>
    /// Whether this is the database being unusable, rather than a bug in the code
    /// above it. A logic error must not be quietly reported as a storage problem.
    /// </summary>
    private static bool IsStorageFailure(Exception ex) =>
        ex is SqliteException or IOException or UnauthorizedAccessException;
}
