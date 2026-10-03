using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

/// <summary>
/// The stored animation preferences are keys, and every one of them has to
/// survive a database that names a value this build no longer knows.
/// </summary>
public sealed class WidgetAnimationSettingsTests
{
    [Theory]
    [InlineData(WidgetAnimationSetting.Slide, true)]
    [InlineData(WidgetAnimationSetting.None, false)]
    [InlineData("SomethingElse", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void OnlyTheStoredOffKeyTurnsTheAnimationOff(string? stored, bool expected) =>
        Assert.Equal(expected, WidgetAnimationSetting.IsAnimated(stored));

    [Fact]
    public void AnUnrecognisedKeyFallsBackToTheShippedDefault()
    {
        Assert.Equal(WidgetAnimationSetting.Slide, WidgetAnimationSetting.Normalize("Fade"));
        Assert.Equal(WidgetAnimationSpeedSetting.Standard, WidgetAnimationSpeedSetting.Normalize("Instant"));
        Assert.Equal(WidgetAnimationDirectionSetting.Right, WidgetAnimationDirectionSetting.Normalize("Diagonal"));
        Assert.Equal(WidgetAnimationEasingSetting.Standard, WidgetAnimationEasingSetting.Normalize("Spring"));
    }

    [Fact]
    public void EveryOfferedKeyIsRecognised()
    {
        foreach (var key in WidgetAnimationSetting.Offered)
            Assert.Equal(key, WidgetAnimationSetting.Normalize(key));

        foreach (var key in WidgetAnimationSpeedSetting.Offered)
            Assert.Equal(key, WidgetAnimationSpeedSetting.Normalize(key));

        foreach (var key in WidgetAnimationDirectionSetting.Offered)
            Assert.Equal(key, WidgetAnimationDirectionSetting.Normalize(key));

        foreach (var key in WidgetAnimationEasingSetting.Offered)
            Assert.Equal(key, WidgetAnimationEasingSetting.Normalize(key));
    }

    [Fact]
    public void ASlowerChoiceAlwaysLastsLonger()
    {
        var durations = WidgetAnimationSpeedSetting.Offered
            .Select(WidgetAnimationSpeedSetting.DurationMs)
            .ToArray();

        // The list is the order the settings window shows them in, so it is also
        // the order they have to read in: fastest first. The gap between the middle
        // and the slow end is the one that matters — a widget crossing a whole
        // screen has to have somewhere to go when the default feels too quick.
        Assert.Equal(durations.OrderBy(duration => duration), durations);
        Assert.Equal([120, 220, 240, 520, 680], durations);
    }

    [Fact]
    public void TheStoredDefaultsAreWhatTheProductShips()
    {
        var settings = new AppSettings();

        Assert.Equal(WidgetAnimationSetting.Slide, settings.WidgetsAnimation);
        Assert.Equal(WidgetAnimationSpeedSetting.Standard, settings.WidgetAnimationSpeed);
        Assert.Equal(WidgetAnimationDirectionSetting.Right, settings.WidgetAnimationDirection);
        Assert.Equal(WidgetAnimationEasingSetting.Standard, settings.WidgetAnimationEasing);
    }
}
