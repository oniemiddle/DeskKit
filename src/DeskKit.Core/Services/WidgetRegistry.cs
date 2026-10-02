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

    /// <summary>
    /// Builds a catalogue from the providers registered by the composition root.
    /// Keeping the catalogue in Core means the shell does not need to know whether
    /// a provider is built in, supplied by an extension, or loaded by a future
    /// plugin host.
    /// </summary>
    public WidgetRegistry(IEnumerable<IWidgetProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        foreach (var provider in providers)
            Register(provider);
    }

    /// <summary>Creates an empty catalogue for hosts that register providers later.</summary>
    public WidgetRegistry()
    {
    }

    public IReadOnlyList<IWidgetProvider> Providers => _ordered;

    public void Register(IWidgetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!TryRegister(provider))
            throw new ArgumentException(
                $"A widget provider with ID '{provider.Descriptor.Id}' is already registered.",
                nameof(provider));
    }

    /// <summary>
    /// Registers a provider when its ID has not already been claimed.
    /// Use this only for an optional provider whose absence is acceptable; normal
    /// application composition should use <see cref="Register"/> so a plugin ID
    /// collision fails at startup instead of silently hiding functionality.
    /// </summary>
    public bool TryRegister(IWidgetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var id = provider.Descriptor.Id;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A widget provider must declare a non-empty ID.", nameof(provider));

        if (!_byId.TryAdd(id, provider))
            return false;

        _ordered.Add(provider);
        return true;
    }

    public IWidgetProvider? Find(string widgetId) =>
        _byId.GetValueOrDefault(widgetId);
}
