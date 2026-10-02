using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class WidgetSeedPolicyTests
{
    private static readonly IReadOnlyList<ScreenBounds> OneScreen = [new ScreenBounds(0, 0, 1920, 1080)];

    [Fact]
    public void APlacementTakesItsIdentityAndItsStartingSizeFromTheDescriptor()
    {
        var provider = new StubProvider("clock", defaultWidth: 260, defaultHeight: 130);

        var placement = WidgetSeedPolicy.CreatePlacement(provider, 0, OneScreen, "abc");

        Assert.Equal("abc", placement.InstanceId);
        Assert.Equal("clock", placement.WidgetId);
        Assert.Equal(260, placement.Width);
        Assert.Equal(130, placement.Height);
        Assert.True(placement.Enabled);
    }

    [Fact]
    public void TheFirstWidgetIsOffsetFromTheScreensOwnOrigin()
    {
        var provider = new StubProvider("clock");

        var placement = WidgetSeedPolicy.CreatePlacement(provider, 0, OneScreen, "id");

        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.X);
        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.Y);
    }

    [Fact]
    public void TheOffsetFollowsTheScreenRatherThanTheDesktopOrigin()
    {
        var provider = new StubProvider("clock");
        IReadOnlyList<ScreenBounds> second = [new ScreenBounds(1920, 0, 1920, 1080)];

        var placement = WidgetSeedPolicy.CreatePlacement(provider, 0, second, "id");

        Assert.Equal(1920 + WidgetSeedPolicy.InitialOffset, placement.X);
        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.Y);
    }

    [Fact]
    public void WidgetsCreatedInTheSameRunCascadeInsteadOfStacking()
    {
        var provider = new StubProvider("clock");

        var first = WidgetSeedPolicy.CreatePlacement(provider, 0, OneScreen, "a");
        var second = WidgetSeedPolicy.CreatePlacement(provider, 1, OneScreen, "b");
        var third = WidgetSeedPolicy.CreatePlacement(provider, 2, OneScreen, "c");

        Assert.Equal(WidgetSeedPolicy.CascadeStep, second.X - first.X);
        Assert.Equal(WidgetSeedPolicy.CascadeStep, third.X - second.X);
        Assert.Equal(second.X - first.X, second.Y - first.Y);
    }

    [Fact]
    public void TheCascadeRepeatsAfterACycleRatherThanMarchingAcrossTheScreen()
    {
        var provider = new StubProvider("clock");

        var atStart = WidgetSeedPolicy.CreatePlacement(provider, 0, OneScreen, "a");
        var afterOneCycle = WidgetSeedPolicy.CreatePlacement(provider, WidgetSeedPolicy.CascadeCycle, OneScreen, "b");

        Assert.Equal(atStart.X, afterOneCycle.X);
        Assert.Equal(atStart.Y, afterOneCycle.Y);
    }

    [Fact]
    public void WithNoScreensTheWidgetStillGetsAUsablePosition()
    {
        var provider = new StubProvider("clock");

        var placement = WidgetSeedPolicy.CreatePlacement(provider, 0, [], "id");

        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.X);
        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.Y);
    }

    [Fact]
    public void AProviderWithoutMigrationsIsStampedWithTheFirstVersion()
    {
        var provider = new StubProvider("clock");

        var placement = WidgetSeedPolicy.CreatePlacement(provider, 0, OneScreen, "id");

        Assert.Equal(1, placement.SettingsVersion);
    }

    [Fact]
    public void AProviderThatDeclaresAVersionIsStampedWithIt()
    {
        // A widget created now is already written in today's shape, so it must not be
        // stamped with a version it did not come from.
        var provider = new MigratingProvider("launcher", version: 2);

        var placement = WidgetSeedPolicy.CreatePlacement(provider, 0, OneScreen, "id");

        Assert.Equal(2, placement.SettingsVersion);
    }

    [Fact]
    public void AFirstRunLayoutProducesOnePlacementPerWidgetInOrder()
    {
        var registry = new WidgetRegistry([
            new StubProvider("clock"),
            new StubProvider("note"),
            new StubProvider("launcher"),
        ]);

        var placements = WidgetSeedPolicy.CreateFirstRunPlacements(
            ["clock", "note", "launcher"],
            registry,
            OneScreen,
            NewId);

        Assert.Equal(["clock", "note", "launcher"], placements.Select(p => p.WidgetId));
        Assert.Equal(3, placements.Select(p => p.InstanceId).Distinct().Count());
    }

    [Fact]
    public void AFirstRunLayoutCascadesItsWidgets()
    {
        var registry = new WidgetRegistry([
            new StubProvider("clock"),
            new StubProvider("note"),
        ]);

        var placements = WidgetSeedPolicy.CreateFirstRunPlacements(
            ["clock", "note"],
            registry,
            OneScreen,
            NewId);

        Assert.Equal(WidgetSeedPolicy.CascadeStep, placements[1].X - placements[0].X);
    }

    [Fact]
    public void ALayoutNamingAWidgetThisBuildDoesNotHaveSkipsIt()
    {
        // A stored layout can name a widget a later build dropped, and that must not
        // stop the rest of the layout from appearing.
        var registry = new WidgetRegistry([new StubProvider("clock")]);

        var placements = WidgetSeedPolicy.CreateFirstRunPlacements(
            ["gone", "clock", "also-gone"],
            registry,
            OneScreen,
            NewId);

        var placement = Assert.Single(placements);
        Assert.Equal("clock", placement.WidgetId);
        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.X);
    }

    [Fact]
    public void AnEmptyLayoutProducesNothing()
    {
        var placements = WidgetSeedPolicy.CreateFirstRunPlacements(
            [],
            new WidgetRegistry([new StubProvider("clock")]),
            OneScreen,
            NewId);

        Assert.Empty(placements);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private sealed class StubProvider(
        string id, double defaultWidth = 100, double defaultHeight = 100) : IWidgetProvider
    {
        public WidgetDescriptor Descriptor { get; } =
            new(id, id, null, defaultWidth, defaultHeight, 40, 40, true);

        public WidgetViewModel Create(WidgetContext context) => throw new NotSupportedException();
    }

    private sealed class MigratingProvider(string id, int version) : IWidgetProvider, IWidgetSettingsMigrations
    {
        public WidgetDescriptor Descriptor { get; } = new(id, id, null, 100, 100, 40, 40, true);

        public int SettingsVersion { get; } = version;

        public IReadOnlyList<WidgetSettingsMigration> Migrations { get; } = [];

        public WidgetViewModel Create(WidgetContext context) => throw new NotSupportedException();
    }
}
