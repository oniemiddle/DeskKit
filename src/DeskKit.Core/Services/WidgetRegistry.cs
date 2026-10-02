using DeskKit.Core.Abstractions;

namespace DeskKit.Core.Services;

/// <summary>
/// The set of widget types the application knows about, in the order they
/// should appear in the "add widget" menu.
/// </summary>
public sealed class WidgetRegistry
{
    private readonly Dictionary<string, IWidgetProvider> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IWidgetProvider> _ordered = [];

    public IReadOnlyList<IWidgetProvider> Providers => _ordered;

    public void Register(IWidgetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (_byId.TryAdd(provider.Descriptor.Id, provider))
        {
            _ordered.Add(provider);
        }
    }

    public IWidgetProvider? Find(string widgetId) =>
        _byId.GetValueOrDefault(widgetId);
}
