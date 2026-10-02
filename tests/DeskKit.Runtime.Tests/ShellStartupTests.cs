using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using DeskKit.Runtime;

namespace DeskKit.Runtime.Tests;

/// <summary>
/// The work the shell does once at startup: what a first run is given, and which stored
/// settings get rewritten on the way in. Both are product policy that used to be private
/// methods on the shell, so they are checked here rather than through a live window.
/// </summary>
public sealed class ShellStartupTests
{
    /// <summary>Any widget id: the runtime never knows which ones a build ships.</summary>
    private const string ClockId = "test.clock";

    private const string SettingKey = "value";

    private static readonly ScreenBounds Primary = new(0, 0, 1920, 1080);

    [Fact]
    public void AFirstRunIsGivenTheIdsItWasHanded()
    {
        var startup = StartupWith(Provider());

        var placement = Assert.Single(startup.FirstRunPlacements([Primary]));

        Assert.Equal(ClockId, placement.WidgetId);

        // Placed inside the first screen rather than at its corner, and stamped with the
        // version its provider already writes so it never has to be migrated.
        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.X);
        Assert.Equal(WidgetSeedPolicy.InitialOffset, placement.Y);
        Assert.Equal(Provider().Descriptor.DefaultWidth, placement.Width);
        Assert.Equal(1, placement.SettingsVersion);
        Assert.False(string.IsNullOrWhiteSpace(placement.InstanceId));
    }

    [Fact]
    public void AFirstRunSkipsALayoutEntryThisBuildDoesNotHave()
    {
        // No provider registered at all: the default layout names a widget this build
        // cannot make, which has to leave an empty desktop rather than throwing.
        var startup = new ShellStartup(new WidgetRegistry(), [ClockId], NullLogger.Instance);

        Assert.Empty(startup.FirstRunPlacements([Primary]));
    }

    [Fact]
    public void AFirstRunWithoutScreensStillProducesOnePlacementPerEntry()
    {
        // A machine that reports no displays must still get its widgets: they end up at
        // the origin and the placement normaliser moves them onto a screen that appears.
        var startup = StartupWith(Provider());

        Assert.Single(startup.FirstRunPlacements([]));
    }

    [Fact]
    public void StoredSettingsAlreadyAtTheirVersionNeedNoMigration()
    {
        var startup = StartupWith(new MigratingProvider(version: 2));

        Assert.Null(startup.MigrateWidgetSettings([Placed(version: 2)]));
    }

    [Fact]
    public void StoredSettingsBehindTheirProvidersVersionAreBroughtForward()
    {
        var startup = StartupWith(new MigratingProvider(version: 2));

        var result = startup.MigrateWidgetSettings([Placed(version: 1)]);

        var placement = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<WidgetPlacement>>(result));
        Assert.Equal(2, placement.SettingsVersion);
        Assert.Equal(7, placement.Settings[SettingKey].GetInt32());
    }

    [Fact]
    public void StoredSettingsFromANewerBuildAreLeftAlone()
    {
        var startup = StartupWith(new MigratingProvider(version: 2));

        Assert.Null(startup.MigrateWidgetSettings([Placed(version: 5)]));
    }

    [Fact]
    public void AWidgetThisBuildDoesNotHaveIsLeftAlone()
    {
        var startup = new ShellStartup(new WidgetRegistry(), [ClockId], NullLogger.Instance);

        Assert.Null(startup.MigrateWidgetSettings([Placed(version: 1)]));
    }

    private static WidgetPlacement Placed(int version) => new()
    {
        InstanceId = "instance-1",
        WidgetId = ClockId,
        SettingsVersion = version,
    };

    private static ShellStartup StartupWith(IWidgetProvider provider)
    {
        var registry = new WidgetRegistry();
        registry.Register(provider);
        return new ShellStartup(registry, [ClockId], NullLogger.Instance);
    }

    private static IWidgetProvider Provider() =>
        new PlainProvider(new WidgetDescriptor(
            ClockId, "Clock_Name", "Clock_Description", 240, 120, 120, 80, PreventActivation: true));

    /// <summary>A provider with no settings history: version 1 and nothing to run.</summary>
    private sealed class PlainProvider(WidgetDescriptor descriptor) : IWidgetProvider
    {
        public WidgetDescriptor Descriptor { get; } = descriptor;

        public WidgetViewModel Create(WidgetContext context) =>
            throw new NotSupportedException("Nothing in this test creates a widget.");
    }

    /// <summary>A provider whose settings have moved on once, writing a known value.</summary>
    private sealed class MigratingProvider(int version) : IWidgetProvider, IWidgetSettingsMigrations
    {
        public WidgetDescriptor Descriptor { get; } = new(
            ClockId, "Clock_Name", "Clock_Description", 240, 120, 120, 80, PreventActivation: true);

        public int SettingsVersion { get; } = version;

        public IReadOnlyList<WidgetSettingsMigration> Migrations { get; } =
            [new WidgetSettingsMigration(1, settings => settings.Set(SettingKey, 7))];

        public WidgetViewModel Create(WidgetContext context) =>
            throw new NotSupportedException("Nothing in this test creates a widget.");
    }
}