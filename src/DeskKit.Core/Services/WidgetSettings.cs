using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskKit.Core.Services;

/// <summary>
/// A widget instance's own configuration, stored as a set of JSON values so the
/// shell never has to know what any particular widget persists.
/// <para>
/// Reads always have a fallback, so a missing or unreadable value degrades to
/// the widget's default instead of throwing.
/// </para>
/// </summary>
public sealed class WidgetSettings : INotifyPropertyChanged
{
    private readonly Dictionary<string, JsonElement> _values;

    public WidgetSettings(Dictionary<string, JsonElement> values) => _values = values;

    public event PropertyChangedEventHandler? PropertyChanged;

    public T Get<T>(string key, T fallback)
    {
        if (!_values.TryGetValue(key, out var element))
            return fallback;

        try
        {
            return element.Deserialize<T>() ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    public void Set<T>(string key, T value)
    {
        var element = JsonSerializer.SerializeToElement(value);

        if (_values.TryGetValue(key, out var existing) && existing.GetRawText() == element.GetRawText())
            return;

        _values[key] = element;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }

    /// <summary>A snapshot of the current values, for persistence.</summary>
    public Dictionary<string, JsonElement> ToDictionary() => new(_values);

    /// <summary>Replaces a value with a JSON object literal, for nested settings.</summary>
    public void SetJson(string key, JsonNode? node)
    {
        if (node is null)
        {
            if (_values.Remove(key))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));

            return;
        }

        var element = JsonSerializer.SerializeToElement(node);
        _values[key] = element;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }
}
