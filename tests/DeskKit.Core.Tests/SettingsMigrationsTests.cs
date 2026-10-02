using System.Text.Json;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class SettingsMigrationsTests
{
    private static WidgetSettings Settings(params (string Key, object Value)[] entries)
    {
        var values = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in entries)
            values[key] = JsonSerializer.SerializeToElement(value);

        return new WidgetSettings(values);
    }

    private static WidgetSettingsMigration Step(int from, List<int> ran) =>
        new(from, _ => ran.Add(from));

    [Fact]
    public void Apply_AtTheTargetVersion_DoesNothing()
    {
        var ran = new List<int>();

        var version = SettingsMigrations.Apply(
            Settings(), 3, 3, [Step(1, ran), Step(2, ran)], out var problem);

        Assert.Null(problem);
        Assert.Equal(3, version);
        Assert.Empty(ran);
    }

    [Fact]
    public void Apply_RunsEveryStepInOrderAndReportsTheTargetVersion()
    {
        var ran = new List<int>();

        var version = SettingsMigrations.Apply(
            Settings(), 1, 4, [Step(3, ran), Step(1, ran), Step(2, ran)], out var problem);

        Assert.Null(problem);
        Assert.Equal(4, version);
        Assert.Equal([1, 2, 3], ran);
    }

    [Fact]
    public void Apply_RunsOnlyTheStepsFromTheStoredVersionOnwards()
    {
        var ran = new List<int>();

        var version = SettingsMigrations.Apply(
            Settings(), 2, 4, [Step(1, ran), Step(2, ran), Step(3, ran)], out var problem);

        Assert.Null(problem);
        Assert.Equal(4, version);
        Assert.Equal([2, 3], ran);
    }

    [Fact]
    public void Apply_WithAGap_ReportsItAndChangesNothing()
    {
        var ran = new List<int>();
        var settings = Settings(("name", "before"));

        var version = SettingsMigrations.Apply(
            settings, 1, 3, [Step(1, ran), Step(3, ran)], out var problem);

        // The gap is found before any step runs. A migration that had already
        // rewritten the settings could not be undone, and the retry would run it
        // twice.
        Assert.Equal(1, version);
        Assert.NotNull(problem);
        Assert.Empty(ran);
        Assert.Equal("before", settings.Get("name", string.Empty));
    }

    [Fact]
    public void Apply_WithAStepMissingEntirely_ReportsItAndKeepsTheVersion()
    {
        var ran = new List<int>();

        var version = SettingsMigrations.Apply(Settings(), 1, 2, [], out var problem);

        Assert.Equal(1, version);
        Assert.NotNull(problem);
        Assert.Empty(ran);
    }

    [Fact]
    public void Apply_WithTwoStepsStartingAtTheSameVersion_ReportsItAndChangesNothing()
    {
        var ran = new List<int>();

        var version = SettingsMigrations.Apply(
            Settings(), 1, 2, [Step(1, ran), Step(1, ran)], out var problem);

        Assert.Equal(1, version);
        Assert.NotNull(problem);
        Assert.Empty(ran);
    }

    [Fact]
    public void Apply_FromANewerVersionThanTheBuildWrites_ChangesNothing()
    {
        var ran = new List<int>();

        var version = SettingsMigrations.Apply(
            Settings(), 5, 2, [Step(1, ran), Step(2, ran), Step(3, ran), Step(4, ran)],
            out var problem);

        // A widget downgraded to an older build keeps its settings and its version,
        // so upgrading again migrates exactly once.
        Assert.Equal(5, version);
        Assert.NotNull(problem);
        Assert.Empty(ran);
    }

    [Fact]
    public void Apply_ActuallyRewritesTheSettingsItIsGiven()
    {
        var settings = Settings(("paper", 2));

        var version = SettingsMigrations.Apply(
            settings,
            1,
            2,
            [new WidgetSettingsMigration(1, s => s.Set("paper", s.Get("paper", 0) + 1))],
            out var problem);

        Assert.Null(problem);
        Assert.Equal(2, version);
        Assert.Equal(3, settings.Get("paper", 0));
    }
}
