using System.Text.Json;
using DeskKit.Core.Abstractions;
using DeskKit.Widgets;

namespace DeskKit.App.Tests;

/// <summary>
/// Every resource key a built-in widget's descriptor names must exist in every language
/// file, because a missing key does not fail: it renders as the key itself, or as
/// nothing, and both are quiet.
/// </summary>
/// <remarks>
/// The same thing is checked while the desktop self test runs, but that needs a machine
/// with a desktop and is not part of the build, so a key deleted from one language file
/// would otherwise only be noticed by whoever looked at that language.
/// </remarks>
public sealed class WidgetResourceKeyTests
{
    private static readonly string[] Languages = ["Strings.json", "Strings.zh-Hans.json"];

    public static TheoryData<string> WidgetKeys()
    {
        var data = new TheoryData<string>();

        foreach (var key in DescribedKeys())
            data.Add(key);

        return data;
    }

    private static IReadOnlyList<string> DescribedKeys()
    {
        var keys = new List<string>();

        foreach (var provider in BuiltInWidgets.CreateProviders(new NullShellIconLoader()))
        {
            keys.Add(provider.Descriptor.DisplayName);

            if (provider.Descriptor.Description is { } description)
                keys.Add(description);
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(WidgetKeys))]
    public void EveryWidgetKeyExistsInEveryLanguage(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(key), "a descriptor named an empty key");

        foreach (var language in Languages)
        {
            using var document = JsonDocument.Parse(Read(language));

            Assert.True(
                TryResolve(document.RootElement, key, out var value),
                $"{language} has no value for {key}");

            Assert.False(
                string.IsNullOrWhiteSpace(value),
                $"{language} has an empty value for {key}");
        }
    }

    [Fact]
    public void TheKeysCoverEveryBuiltInWidget()
    {
        // A guard on the check above: if the catalogue ever stops describing a widget,
        // the theory would quietly have nothing to assert.
        var names = DescribedKeys().Count(key => key.EndsWith("_Name", StringComparison.Ordinal));

        Assert.Equal(
            BuiltInWidgets.CreateProviders(new NullShellIconLoader()).Count,
            names);
    }

    [Fact]
    public void TheCheckCanFail()
    {
        // The theory is only worth having if a key that is not there is reported as not
        // there: a resolver that quietly answered "found" would make it unfalsifiable.
        using var document = JsonDocument.Parse(Read("Strings.json"));

        Assert.False(TryResolve(document.RootElement, "Widget_Clock_NoSuchKey", out _));
        Assert.False(TryResolve(document.RootElement, "NoSuchSection_Name", out _));

        Assert.True(TryResolve(document.RootElement, "Widget_Clock_Name", out var value));
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    /// <summary>Resolves <c>Widget_Clock_Name</c> against the nested <c>Widget.Clock.Name</c>.</summary>
    private static bool TryResolve(JsonElement root, string key, out string? value)
    {
        value = null;
        var node = root;

        foreach (var part in key.Split('_'))
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out var next))
                return false;

            node = next;
        }

        value = node.ValueKind == JsonValueKind.String ? node.GetString() : null;
        return true;
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WidgetResources", fileName));
}