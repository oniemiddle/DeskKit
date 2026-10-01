using System.Text.Json;
using DeskKit.Core.Models;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "deskkit-tests-" + Guid.NewGuid().ToString("N"));

    public ConfigStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ConfigStore CreateStore() => new(_directory);

    [Fact]
    public void Load_WithNoFile_ReturnsDefaultsAndReportsNoFile()
    {
        var store = CreateStore();

        var state = store.Load();

        Assert.False(store.FileExistedOnLoad);
        Assert.Equal(AppState.CurrentVersion, state.Version);
        Assert.Empty(state.Widgets);
        Assert.True(state.Settings.ShowTrayIcon);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        var settings = new Dictionary<string, JsonElement>
        {
            ["use24Hour"] = JsonSerializer.SerializeToElement(false),
            ["timeFontSize"] = JsonSerializer.SerializeToElement(42),
            ["title"] = JsonSerializer.SerializeToElement("买牛奶"),
        };

        var original = new AppState
        {
            Settings = new AppSettings
            {
                Theme = "Dark",
                StartWithWindows = true,
                ShowTrayIcon = false,
                WidgetsVisible = false,
            },
            Widgets =
            [
                new WidgetPlacement
                {
                    InstanceId = "abc123",
                    WidgetId = "clock",
                    X = 120,
                    Y = 240,
                    Width = 260,
                    Height = 130,
                    Settings = settings,
                },
            ],
        };

        var store = CreateStore();
        store.Save(original);

        var reloaded = CreateStore().Load();

        Assert.True(CreateStore().Load().Version == AppState.CurrentVersion);
        Assert.Equal("Dark", reloaded.Settings.Theme);
        Assert.True(reloaded.Settings.StartWithWindows);
        Assert.False(reloaded.Settings.ShowTrayIcon);
        Assert.False(reloaded.Settings.WidgetsVisible);

        var widget = Assert.Single(reloaded.Widgets);
        Assert.Equal("abc123", widget.InstanceId);
        Assert.Equal("clock", widget.WidgetId);
        Assert.Equal(120, widget.X);
        Assert.Equal(240, widget.Y);
        Assert.Equal(260, widget.Width);

        var wrapper = new WidgetSettings(widget.Settings);
        Assert.False(wrapper.Get("use24Hour", true));
        Assert.Equal(42d, wrapper.Get("timeFontSize", 0d));
        Assert.Equal("买牛奶", wrapper.Get("title", string.Empty));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var store = CreateStore();
        store.Save(new AppState());

        Assert.True(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void Load_WithCorruptFile_MovesItAsideAndReturnsDefaults()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ this is not json");

        var state = store.Load();

        Assert.Empty(state.Widgets);
        Assert.NotNull(store.LastCorruptFileBackup);
        Assert.NotEmpty(store.LastCorruptFileBackup!);

        // The original file is preserved for the user rather than deleted.
        Assert.True(File.Exists(store.LastCorruptFileBackup!));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Load_WithEmptyFile_ReturnsDefaultsWithoutBackup()
    {
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "   ");

        var state = store.Load();

        Assert.Empty(state.Widgets);
        Assert.Null(store.LastCorruptFileBackup);
    }

    [Fact]
    public void Save_ThenLoad_ReportsThatTheFileExisted()
    {
        var store = CreateStore();
        store.Save(new AppState());

        var second = CreateStore();
        second.Load();

        Assert.True(second.FileExistedOnLoad);
    }

    [Fact]
    public void Migrate_BringsAnOlderVersionForward()
    {
        var migrated = ConfigStore.Migrate(new AppState { Version = 0 });

        Assert.Equal(AppState.CurrentVersion, migrated.Version);
    }

    [Fact]
    public void Migrate_KeepsWidgetsWhenTheVersionIsUnknown()
    {
        var state = new AppState
        {
            Version = 99,
            Widgets = [new WidgetPlacement { InstanceId = "x", WidgetId = "clock" }],
        };

        var migrated = ConfigStore.Migrate(state);

        // Losing a user's layout is worse than reading a field a newer build wrote.
        Assert.Equal(AppState.CurrentVersion, migrated.Version);
        Assert.Single(migrated.Widgets);
    }
}
