using System.Text.Json;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class WidgetSettingsMigratorTests
{
    [Fact]
    public void AWidgetThatDeclaresNoVersionsIsCarriedThroughUntouched()
    {
        var placement = Placement("note", version: 1);
        var registry = new WidgetRegistry([new StubProvider("note")]);

        var result = WidgetSettingsMigrator.Apply([placement], registry);

        var entry = Assert.Single(result.Entries);
        Assert.Same(placement, entry.Placement);
        Assert.False(entry.Migrated);
        Assert.Null(entry.Problem);
        Assert.False(result.Changed);
    }

    [Fact]
    public void AWidgetWithNothingToMigrateIsCarriedThroughUntouched()
    {
        var placement = Placement("launcher", version: 2);
        var registry = new WidgetRegistry([new MigratingProvider("launcher", version: 2)]);

        var result = WidgetSettingsMigrator.Apply([placement], registry);

        var entry = Assert.Single(result.Entries);
        Assert.False(entry.Migrated);
        Assert.Null(entry.Problem);
        Assert.Equal(2, entry.Version);
        Assert.False(result.Changed);
    }

    [Fact]
    public void SettingsAreBroughtForwardOneStepAtATime()
    {
        var placement = Placement("launcher", version: 1);
        var registry = new WidgetRegistry([
            new MigratingProvider(
                "launcher",
                version: 3,
                new WidgetSettingsMigration(1, settings => settings.Set("step", "one")),
                new WidgetSettingsMigration(2, settings => settings.Set("step", "two"))),
        ]);

        var result = WidgetSettingsMigrator.Apply([placement], registry);

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.Migrated);
        Assert.True(result.Changed);
        Assert.Equal(3, entry.Version);
        Assert.Equal(3, entry.Placement.SettingsVersion);

        // The second step ran after the first, and it wrote into the dictionary the
        // placement itself holds - which is the object the widget will be given.
        Assert.Equal("two", entry.Placement.Settings["step"].GetString());
        Assert.Same(placement.Settings, entry.Placement.Settings);
    }

    [Fact]
    public void AMissingStepLeavesTheVersionWhereItWas()
    {
        // 1 -> 2 exists, 2 -> 3 does not. Nothing may run, because a step that has
        // already rewritten the settings cannot be undone.
        var placement = Placement("launcher", version: 1);
        var registry = new WidgetRegistry([
            new MigratingProvider(
                "launcher",
                version: 3,
                new WidgetSettingsMigration(1, settings => settings.Set("step", "one"))),
        ]);

        var result = WidgetSettingsMigrator.Apply([placement], registry);

        var entry = Assert.Single(result.Entries);
        Assert.False(entry.Migrated);
        Assert.False(result.Changed);
        Assert.NotNull(entry.Problem);
        Assert.Equal(1, entry.Placement.SettingsVersion);
        Assert.False(entry.Placement.Settings.ContainsKey("step"));
    }

    [Fact]
    public void SettingsWrittenByANewerBuildAreLeftAlone()
    {
        var placement = Placement("launcher", version: 5);
        var registry = new WidgetRegistry([
            new MigratingProvider("launcher", version: 2, new WidgetSettingsMigration(1, _ => { })),
        ]);

        var result = WidgetSettingsMigrator.Apply([placement], registry);

        var entry = Assert.Single(result.Entries);
        Assert.False(entry.Migrated);
        Assert.NotNull(entry.Problem);
        Assert.Equal(5, entry.Placement.SettingsVersion);
    }

    [Fact]
    public void TwoStepsStartingAtTheSameVersionAreRejectedWithoutRunningEither()
    {
        var placement = Placement("launcher", version: 1);
        var registry = new WidgetRegistry([
            new MigratingProvider(
                "launcher",
                version: 3,
                new WidgetSettingsMigration(1, settings => settings.Set("a", "1")),
                new WidgetSettingsMigration(1, settings => settings.Set("b", "1"))),
        ]);

        var result = WidgetSettingsMigrator.Apply([placement], registry);

        var entry = Assert.Single(result.Entries);
        Assert.False(entry.Migrated);
        Assert.NotNull(entry.Problem);
        Assert.Empty(entry.Placement.Settings);
    }

    [Fact]
    public void OnePassHandlesAWholeLayoutAndKeepsItsOrder()
    {
        var untouched = Placement("clock", version: 1);
        var promoted = Placement("launcher", version: 1);
        var unknown = Placement("from-another-build", version: 1);

        var registry = new WidgetRegistry([
            new StubProvider("clock"),
            new MigratingProvider(
                "launcher",
                version: 2,
                new WidgetSettingsMigration(1, settings => settings.Set("shape", "v2"))),
        ]);

        var result = WidgetSettingsMigrator.Apply([untouched, promoted, unknown], registry);

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(["clock", "launcher", "from-another-build"], result.Placements.Select(p => p.WidgetId));
        Assert.True(result.Changed);

        // Only the launcher moved; the other two are the very same records.
        Assert.Same(untouched, result.Placements[0]);
        Assert.Equal(2, result.Placements[1].SettingsVersion);
        Assert.Same(unknown, result.Placements[2]);
        Assert.True(result.Entries[1].Migrated);
        Assert.False(result.Entries[0].Migrated);
        Assert.False(result.Entries[2].Migrated);

        // The caller decides when to save, so an untouched run is recognisable.
        Assert.NotNull(unknown.WidgetId);
    }

    [Fact]
    public void AnEmptyLayoutIsNoWorkAndNoSave()
    {
        var result = WidgetSettingsMigrator.Apply([], new WidgetRegistry());

        Assert.Empty(result.Entries);
        Assert.Empty(result.Placements);
        Assert.False(result.Changed);
    }

    private static WidgetPlacement Placement(string widgetId, int version) =>
        new()
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            WidgetId = widgetId,
            SettingsVersion = version,
            Settings = [],
        };

    private sealed class StubProvider(string id) : IWidgetProvider
    {
        public WidgetDescriptor Descriptor { get; } = new(id, id, null, 100, 100, 40, 40, true);

        public WidgetViewModel Create(WidgetContext context) => throw new NotSupportedException();
    }

    private sealed class MigratingProvider(
        string id, int version, params WidgetSettingsMigration[] steps)
        : IWidgetProvider, IWidgetSettingsMigrations
    {
        public WidgetDescriptor Descriptor { get; } = new(id, id, null, 100, 100, 40, 40, true);

        public int SettingsVersion { get; } = version;

        public IReadOnlyList<WidgetSettingsMigration> Migrations { get; } = steps;

        public WidgetViewModel Create(WidgetContext context) => throw new NotSupportedException();
    }
}
