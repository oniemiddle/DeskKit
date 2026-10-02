using System.ComponentModel;
using System.Text.Json;

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
    /// <summary>
    /// The options widget settings are written and read with.
    /// </summary>
    /// <remarks>
    /// The naming policy matches the document the values are buried in, so a nested
    /// object does not come out in a different style from everything around it.
    /// Reading stays case-insensitive, because values written before that was
    /// settled are still out there.
    /// <para>
    /// Both directions are reflection based: <see cref="Get{T}"/> and
    /// <see cref="Set{T}"/> are open generics, so no source generated contract can be
    /// closed over a type the shell never sees. This is the one place in the shell
    /// that enabling trimming would break, which is why it stays off until a widget
    /// hands its settings type over as a <c>JsonTypeInfo</c>.
    /// </para>
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, JsonElement> _values;

    public WidgetSettings(Dictionary<string, JsonElement> values) => _values = values;

    public event PropertyChangedEventHandler? PropertyChanged;

    public T Get<T>(string key, T fallback)
    {
        if (!_values.TryGetValue(key, out var element))
            return fallback;

        try
        {
            return element.Deserialize<T>(Options) ?? fallback;
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
        var element = JsonSerializer.SerializeToElement(value, Options);

        if (_values.TryGetValue(key, out var existing) && existing.GetRawText() == element.GetRawText())
            return;

        _values[key] = element;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }
}
