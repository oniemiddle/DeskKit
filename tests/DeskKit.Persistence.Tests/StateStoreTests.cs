using System.Globalization;
using System.Text.Json;
using DeskKit.Persistence;
using DeskKit.Persistence.Data;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DeskKit.Persistence.Tests;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "deskkit-store-" + Guid.NewGuid().ToString("N"));

    /// <summary>Where the database goes, as on a real machine.</summary>
    private readonly string _localDirectory;

    /// <summary>Where the JSON configuration would be, as on a real machine.</summary>
    private readonly string _roamingDirectory;

    public StateStoreTests()
    {
        _localDirectory = Path.Combine(_root, "local");
        _roamingDirectory = Path.Combine(_root, "roaming");

        Directory.CreateDirectory(_localDirectory);
        Directory.CreateDirectory(_roamingDirectory);
    }

    public void Dispose()
    {
        // The provider pools connections, so a database file stays open —and cannot
        // be deleted —until the pool for it is cleared.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string DatabasePath => Path.Combine(_localDirectory, AppPaths.DatabaseFileName);

    private StateStore CreateStore() => new(DatabasePath, _roamingDirectory);

    // ---- First run and round trips ---------------------------------------

    [Fact]
    public void A_first_run_reports_itself_and_creates_the_database()
    {
        var store = CreateStore();
        var state = store.Load();

        Assert.Equal(StoreOutcome.FirstRun, store.LoadReport.Outcome);
        Assert.False(store.LoadReport.HasProblems);
        Assert.False(store.HasStoredState);
        Assert.Empty(state.Widgets);
        Assert.Equal("System", state.Settings.Theme);
        Assert.True(File.Exists(DatabasePath));
    }

    [Fact]
    public void Preferences_and_widgets_survive_a_round_trip()
    {
        var store = CreateStore();
        store.Load();

        var saved = store.Save(new AppState
        {
            Settings = new AppSettings
            {
                Theme = "Dark",
                Language = "zh-Hans",
                StartWithWindows = true,
                ShowTrayIcon = false,
                WidgetsVisible = false,
                DesktopDoubleClickTogglesWidgets = true,
                WidgetsAnimation = WidgetAnimationSetting.None,
                WidgetAnimationSpeed = WidgetAnimationSpeedSetting.Slow,
                WidgetAnimationDirection = WidgetAnimationDirectionSetting.Up,
                WidgetAnimationEasing = WidgetAnimationEasingSetting.Strong,
            },
            Widgets =
            [
                Widget("e49eb887", x: 400, y: 157, widgetId: "clock"),
                Widget("65b35a73", x: 688, y: 340, widgetId: "sticky-note"),
            ],
        });

        Assert.Equal(StoreSaveOutcome.Saved, saved.Outcome);

        var reloaded = CreateStore();
        var state = reloaded.Load();

        Assert.Equal(StoreOutcome.Loaded, reloaded.LoadReport.Outcome);
        Assert.True(reloaded.HasStoredState);
        Assert.Equal("Dark", state.Settings.Theme);
        Assert.Equal("zh-Hans", state.Settings.Language);
        Assert.True(state.Settings.StartWithWindows);
        Assert.False(state.Settings.ShowTrayIcon);
        Assert.False(state.Settings.WidgetsVisible);
        Assert.True(state.Settings.DesktopDoubleClickTogglesWidgets);
        Assert.Equal(WidgetAnimationSetting.None, state.Settings.WidgetsAnimation);
        Assert.Equal(WidgetAnimationSpeedSetting.Slow, state.Settings.WidgetAnimationSpeed);
        Assert.Equal(WidgetAnimationDirectionSetting.Up, state.Settings.WidgetAnimationDirection);
        Assert.Equal(WidgetAnimationEasingSetting.Strong, state.Settings.WidgetAnimationEasing);

        Assert.Equal(2, state.Widgets.Count);
        Assert.Equal("e49eb887", state.Widgets[0].InstanceId);
        Assert.Equal(400, state.Widgets[0].X);
        Assert.Equal(157, state.Widgets[0].Y);
        Assert.Equal("sticky-note", state.Widgets[1].WidgetId);
        Assert.Equal(688, state.Widgets[1].X);
    }

    [Fact]
    public void A_widgets_own_settings_survive_as_a_document()
    {
        var store = CreateStore();
        store.Load();

        store.Save(new AppState
        {
            Widgets =
            [
                Widget("q", settings: new Dictionary<string, JsonElement>
                {
                    ["fontSize"] = JsonSerializer.SerializeToElement(14),
                    ["items"] = JsonSerializer.SerializeToElement(new[] { "one", "two" }),
                }),
            ],
        });

        var state = CreateStore().Load();

        Assert.Equal(14, state.Widgets[0].Settings["fontSize"].GetInt32());
        Assert.Equal(2, state.Widgets[0].Settings["items"].GetArrayLength());
    }

    [Fact]
    public void Moving_one_widget_writes_only_that_row()
    {
        var store = CreateStore();
        store.Load();

        store.Save(new AppState { Widgets = [Widget("a"), Widget("b")] });
        var moved = store.Save(new AppState { Widgets = [Widget("a", x: 999), Widget("b")] });

        Assert.Equal(StoreSaveOutcome.Saved, moved.Outcome);
        Assert.Equal(1, moved.RowsWritten);
    }

    [Fact]
    public void Saving_an_unchanged_state_writes_nothing()
    {
        var store = CreateStore();
        store.Load();

        var state = new AppState { Widgets = [Widget("a")] };
        Assert.True(store.Save(state).RowsWritten > 0);
        Assert.Equal(0, store.Save(state).RowsWritten);
    }

    [Fact]
    public void Position_stays_in_physical_pixels_and_size_in_logical_ones()
    {
        var store = CreateStore();
        store.Load();

        // A position on a second monitor, and a size that is not a whole number: both
        // units have to come back exactly as they went in.
        var widget = Widget("a", x: 5120, y: 1440) with { Width = 260.5, Height = 130.25 };
        store.Save(new AppState { Widgets = [widget] });

        var state = CreateStore().Load();
        var loaded = Assert.Single(state.Widgets);

        Assert.Equal(5120, loaded.X);
        Assert.Equal(1440, loaded.Y);
        Assert.Equal(260.5, loaded.Width);
        Assert.Equal(130.25, loaded.Height);
    }

    [Fact]
    public void Widgets_come_back_in_the_order_they_were_listed()
    {
        var store = CreateStore();
        store.Load();

        store.Save(new AppState { Widgets = [Widget("c"), Widget("a"), Widget("b")] });

        var state = CreateStore().Load();

        Assert.Equal(["c", "a", "b"], state.Widgets.Select(widget => widget.InstanceId));
    }

    [Fact]
    public void Two_rows_with_the_same_order_still_come_back_the_same_way()
    {
        var store = CreateStore();
        store.Load();
        store.Save(new AppState { Widgets = [Widget("b"), Widget("a")] });

        // Only a hand-edited database can tie, but the listing must not depend on
        // which row came out of the file first.
        Execute(DatabasePath, "UPDATE Widgets SET \"Order\" = 0;");

        var state = CreateStore().Load();

        Assert.Equal(["a", "b"], state.Widgets.Select(widget => widget.InstanceId));
    }

    [Fact]
    public void Removing_a_widget_removes_its_row()
    {
        var store = CreateStore();
        store.Load();

        store.Save(new AppState { Widgets = [Widget("a"), Widget("b")] });
        store.Save(new AppState { Widgets = [Widget("a")] });

        Assert.Equal(1, Scalar<long>(DatabasePath, "SELECT COUNT(*) FROM Widgets"));
        Assert.Single(CreateStore().Load().Widgets);
    }

    [Fact]
    public void A_widgets_settings_version_is_carried_through_untouched()
    {
        var store = CreateStore();
        store.Load();

        store.Save(new AppState { Widgets = [Widget("q") with { SettingsVersion = 4 }] });

        Assert.Equal(4, CreateStore().Load().Widgets[0].SettingsVersion);
    }

    // ---- Importing the old layout ---------------------------------------

    [Fact]
    public void The_split_layout_is_imported_once()
    {
        WriteLegacySettings("Dark");
        WriteLegacyWidget("e49eb887", widgetId: "clock", x: 400, y: 157);
        WriteLegacyWidget("65b35a73", widgetId: "sticky-note", x: 688, y: 340, order: 1);

        var store = CreateStore();
        var state = store.Load();

        Assert.Equal(StoreOutcome.Imported, store.LoadReport.Outcome);
        Assert.False(store.LoadReport.HasProblems);
        Assert.True(store.HasStoredState);
        Assert.Equal("Dark", state.Settings.Theme);
        Assert.Equal(2, state.Widgets.Count);
        Assert.Equal("clock", state.Widgets[0].WidgetId);
        Assert.Contains(AppPaths.SettingsFileName, store.LoadReport.ImportedFrom);

        var second = CreateStore();
        var again = second.Load();

        Assert.Equal(StoreOutcome.Loaded, second.LoadReport.Outcome);
        Assert.Equal(2, again.Widgets.Count);
    }

    [Fact]
    public void The_single_file_layout_is_imported()
    {
        File.WriteAllText(
            Path.Combine(_roamingDirectory, AppPaths.LegacyConfigFileName),
            """
            {
              "version": 1,
              "settings": { "theme": "Light", "language": "en", "showTrayIcon": false },
              "widgets": [
                { "instanceId": "e49eb887", "widgetId": "clock", "enabled": true,
                  "x": 400, "y": 157, "width": 260, "height": 130, "settingsVersion": 1, "settings": {} }
              ]
            }
            """);

        var store = CreateStore();
        var state = store.Load();

        Assert.Equal(StoreOutcome.Imported, store.LoadReport.Outcome);
        Assert.Equal(AppPaths.LegacyConfigFileName, store.LoadReport.ImportedFrom);
        Assert.Equal("Light", state.Settings.Theme);
        Assert.Equal(400, Assert.Single(state.Widgets).X);
    }

    [Fact]
    public void The_single_file_kept_aside_is_used_when_nothing_else_is_there()
    {
        File.WriteAllText(
            Path.Combine(_roamingDirectory, AppPaths.LegacyBackupFileName),
            """
            {
              "version": 1,
              "settings": { "theme": "Dark" },
              "widgets": [
                { "instanceId": "e49eb887", "widgetId": "clock", "enabled": true,
                  "x": 1, "y": 2, "width": 260, "height": 130, "settingsVersion": 1, "settings": {} }
              ]
            }
            """);

        var store = CreateStore();
        var state = store.Load();

        Assert.Equal(StoreOutcome.Imported, store.LoadReport.Outcome);
        Assert.Equal(AppPaths.LegacyBackupFileName, store.LoadReport.ImportedFrom);
        Assert.Single(state.Widgets);
    }

    [Fact]
    public void The_split_layout_outranks_the_single_file()
    {
        WriteLegacySettings("Dark");
        File.WriteAllText(
            Path.Combine(_roamingDirectory, AppPaths.LegacyConfigFileName),
            """{ "version": 1, "settings": { "theme": "Light" }, "widgets": [] }""");

        var state = CreateStore().Load();

        Assert.Equal("Dark", state.Settings.Theme);
    }

    [Fact]
    public void A_damaged_widget_file_is_skipped_and_the_rest_are_imported()
    {
        WriteLegacySettings("Dark");
        WriteLegacyWidget("good", x: 400, y: 157);
        File.WriteAllText(
            Path.Combine(_roamingDirectory, AppPaths.WidgetsDirectoryName, "bad.json"),
            "{ this is not json");

        var store = CreateStore();
        var state = store.Load();

        Assert.Equal(StoreOutcome.Imported, store.LoadReport.Outcome);
        Assert.True(store.LoadReport.HasProblems);
        Assert.Equal("good", Assert.Single(state.Widgets).InstanceId);
        Assert.Contains(store.LoadReport.Problems, problem => problem.Subject == "bad");
        Assert.False(store.LoadReport.IsReadOnly);
    }

    [Fact]
    public void Importing_leaves_the_old_files_exactly_as_they_were()
    {
        WriteLegacySettings("Dark");
        WriteLegacyWidget("good", x: 400, y: 157);
        File.WriteAllText(
            Path.Combine(_roamingDirectory, AppPaths.LegacyConfigFileName),
            """{ "version": 1, "settings": { "theme": "Light" }, "widgets": [] }""");

        var before = Snapshot(_roamingDirectory);

        CreateStore().Load();

        Assert.Equal(before, Snapshot(_roamingDirectory));
    }

    // ---- Refusing to touch what it cannot read --------------------------

    [Fact]
    public void A_database_from_a_newer_build_is_refused_and_left_untouched()
    {
        var store = CreateStore();
        store.Load();
        store.Save(new AppState { Widgets = [Widget("a", x: 400, y: 157)] });

        // Stand in for a build that applied a migration this one has never heard of.
        Execute(
            DatabasePath,
            "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) "
            + "VALUES ('20301231235959_FromTheFuture', '10.0.12');");

        var rowsBefore = Scalar<long>(DatabasePath, "SELECT COUNT(*) FROM Widgets");

        var reader = CreateStore();
        var state = reader.Load();

        Assert.Equal(StoreOutcome.NewerSchema, reader.LoadReport.Outcome);
        Assert.True(reader.LoadReport.HasProblems);
        Assert.True(reader.LoadReport.IsReadOnly);
        Assert.Empty(state.Widgets);

        var saved = reader.Save(new AppState { Widgets = [Widget("b")] });

        Assert.Equal(StoreSaveOutcome.RefusedNewerSchema, saved.Outcome);
        Assert.Equal(0, saved.RowsWritten);
        Assert.Equal(rowsBefore, Scalar<long>(DatabasePath, "SELECT COUNT(*) FROM Widgets"));
        Assert.Equal(0, Scalar<long>(DatabasePath, "SELECT COUNT(*) FROM Widgets WHERE InstanceId = 'b'"));
    }

    [Fact]
    public void A_database_that_cannot_be_opened_is_reported_and_never_written()
    {
        const string notADatabase = "this is not a database";
        File.WriteAllText(DatabasePath, notADatabase);

        var store = CreateStore();
        var state = store.Load();

        Assert.Equal(StoreOutcome.Unavailable, store.LoadReport.Outcome);
        Assert.True(store.LoadReport.HasProblems);
        Assert.True(store.LoadReport.IsReadOnly);
        Assert.Empty(state.Widgets);
        Assert.True(store.HasStoredState);

        var saved = store.Save(new AppState { Widgets = [Widget("a")] });

        Assert.Equal(StoreSaveOutcome.RefusedUnavailable, saved.Outcome);
        Assert.Equal(0, saved.RowsWritten);
        Assert.Equal(notADatabase, File.ReadAllText(DatabasePath));
    }

    // ---- The database itself --------------------------------------------

    [Fact]
    public void The_database_uses_write_ahead_logging_and_flushes_on_commit()
    {
        var store = CreateStore();
        store.Load();
        store.Save(new AppState { Widgets = [Widget("a")] });

        using var context = new DeskKitDbContext(DatabaseOptions.For(DatabasePath));
        context.Database.OpenConnection();

        try
        {
            Assert.Equal("wal", ScalarOnOpenConnection<string>(context, "PRAGMA journal_mode"));

            // 2 is FULL: a commit is not reported as done until it has reached the
            // disk, which is what replaces writing a temporary file and renaming it.
            Assert.Equal(2L, ScalarOnOpenConnection<long>(context, "PRAGMA synchronous"));
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    [Fact]
    public void The_model_and_the_migrations_agree()
    {
        var store = CreateStore();
        store.Load();

        using var context = new DeskKitDbContext(DatabaseOptions.For(DatabasePath));

        // Guards against a property being added to an entity and the migration for it
        // never being generated: the app would then disagree with its own schema.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void A_fresh_database_is_not_backed_up()
    {
        var store = CreateStore();
        store.Load();

        // Nothing has been lost if an empty file is replaced, and copying it would
        // only overwrite a good backup with an empty one.
        Assert.False(File.Exists(store.PreMigrationBackupPath));
    }

    [Fact]
    public void A_backup_still_holds_the_newest_commit_while_the_database_is_open()
    {
        // Write-ahead logging keeps a commit in the -wal file until a checkpoint, so
        // this is the case a plain copy of the database file gets wrong.
        using var source = new SqliteConnection(DatabaseOptions.ConnectionStringFor(DatabasePath));
        source.Open();
        Execute(source, "PRAGMA journal_mode=WAL;");
        Execute(source, "CREATE TABLE Notes (Text TEXT); INSERT INTO Notes (Text) VALUES ('hello');");

        var writeAheadLog = DatabasePath + "-wal";
        Assert.True(File.Exists(writeAheadLog) && new FileInfo(writeAheadLog).Length > 0);

        var plainCopy = Path.Combine(_localDirectory, "plain-copy.db");
        File.Copy(DatabasePath, plainCopy);

        var backupPath = Path.Combine(_localDirectory, "backup.db");
        DatabaseBackup.Write(DatabasePath, backupPath);

        Assert.Equal("hello", Scalar<string>(backupPath, "SELECT Text FROM Notes"));
        Assert.Null(Scalar<string>(plainCopy, "SELECT Text FROM Notes"));
    }

    // ---- Helpers --------------------------------------------------------

    private static WidgetPlacement Widget(
        string instanceId,
        int x = 10,
        int y = 20,
        string widgetId = "clock",
        Dictionary<string, JsonElement>? settings = null) =>
        new()
        {
            InstanceId = instanceId,
            WidgetId = widgetId,
            X = x,
            Y = y,
            Width = 260,
            Height = 130,
            Settings = settings ?? [],
        };

    private void WriteLegacySettings(string theme) =>
        File.WriteAllText(
            Path.Combine(_roamingDirectory, AppPaths.SettingsFileName),
            $$"""
            {
              "version": 2,
              "settings": { "theme": "{{theme}}", "language": "System", "showTrayIcon": true }
            }
            """);

    private void WriteLegacyWidget(
        string instanceId,
        string widgetId = "clock",
        int x = 10,
        int y = 20,
        int order = 0,
        int settingsVersion = 1)
    {
        var directory = Path.Combine(_roamingDirectory, AppPaths.WidgetsDirectoryName);
        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, instanceId + ".json"),
            $$"""
            {
              "version": 1,
              "order": {{order}},
              "widgetId": "{{widgetId}}",
              "enabled": true,
              "x": {{x}},
              "y": {{y}},
              "width": 260,
              "height": 130,
              "settingsVersion": {{settingsVersion}},
              "settings": {}
            }
            """);
    }

    private static SortedDictionary<string, string> Snapshot(string directory)
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            snapshot[relative] = File.ReadAllText(path);
        }

        return snapshot;
    }

    private static void Execute(string databasePath, string sql)
    {
        using var connection = new SqliteConnection(DatabaseOptions.ConnectionStringFor(databasePath));
        connection.Open();
        Execute(connection, sql);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Runs one statement, returning <c>default</c> when the database refuses it —
    /// which is how a copy that is missing its table is told apart from one that has it.
    /// </summary>
    private static T? Scalar<T>(string databasePath, string sql)
    {
        try
        {
            using var connection = new SqliteConnection(DatabaseOptions.ConnectionStringFor(databasePath));
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            var value = command.ExecuteScalar();
            return value is null or DBNull
                ? default
                : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return default;
        }
    }

    private static T? ScalarOnOpenConnection<T>(DbContext context, string sql)
    {
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        var value = command.ExecuteScalar();
        return value is null or DBNull
            ? default
            : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }
}
