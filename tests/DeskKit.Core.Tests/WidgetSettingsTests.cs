using System.Text.Json;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class WidgetSettingsTests
{
    private static Dictionary<string, JsonElement> Values(
        params (string Key, object Value)[] entries)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in entries)
            result[key] = JsonSerializer.SerializeToElement(value);

        return result;
    }

    [Fact]
    public void Get_WithMissingKey_ReturnsFallback()
    {
        var settings = new WidgetSettings([]);

        Assert.True(settings.Get("missing", true));
        Assert.Equal("fallback", settings.Get("missing", "fallback"));
    }

    [Fact]
    public void Get_WithValueOfTheWrongType_ReturnsFallbackInsteadOfThrowing()
    {
        // A hand-edited or older file can easily hold the wrong type here.
        var settings = new WidgetSettings(Values(("flag", "not a bool")));

        Assert.True(settings.Get("flag", true));
    }

    [Fact]
    public void Set_RaisesChangeNotificationForThatKey()
    {
        var settings = new WidgetSettings([]);
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.Set("use24Hour", false);

        Assert.Equal(["use24Hour"], changed);
    }

    [Fact]
    public void Set_WithUnchangedValue_DoesNotNotify()
    {
        var settings = new WidgetSettings(Values(("use24Hour", true)));
        var notified = false;
        settings.PropertyChanged += (_, _) => notified = true;

        settings.Set("use24Hour", true);

        Assert.False(notified);
    }

    [Fact]
    public void ToDictionary_ReturnsACopy()
    {
        var values = Values(("paper", 2));
        var settings = new WidgetSettings(values);

        var copy = settings.ToDictionary();
        copy["paper"] = JsonSerializer.SerializeToElement(5);

        Assert.Equal(2, settings.Get("paper", 0));
    }

    [Fact]
    public void SetJson_RemovesTheKeyWhenGivenNull()
    {
        var settings = new WidgetSettings(Values(("paper", 2)));

        settings.SetJson("paper", null);

        Assert.Equal(-1, settings.Get("paper", -1));
    }

    [Fact]
    public void ChangesAreVisibleThroughTheUnderlyingDictionary()
    {
        // The shell persists the placement's dictionary directly, so writes made
        // through the wrapper have to land in that same instance.
        var values = Values(("paper", 2));
        var settings = new WidgetSettings(values);

        settings.Set("paper", 4);

        Assert.Equal(4, values["paper"].GetInt32());
    }
}
