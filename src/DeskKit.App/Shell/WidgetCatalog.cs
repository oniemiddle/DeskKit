using DeskKit.Core.Abstractions;
using DeskKit.Core.Services;
using DeskKit.Widgets.Localization;

namespace DeskKit.App.Shell;

/// <summary>
/// The widget types this build offers, with the names the user sees.
/// </summary>
/// <remarks>
/// Naming is a product concern: a descriptor carries a resource key so the name can
/// follow a culture change, and resolving that key needs the widgets' own string
/// manager. Keeping it here means the shell that runs widgets never has to know how a
/// widget's name is spelled in the current language.
/// </remarks>
internal sealed class WidgetCatalog(WidgetRegistry registry)
{
    /// <summary>The widget types, in the order they should be offered.</summary>
    public IReadOnlyList<IWidgetProvider> Providers => registry.Providers;

    public IWidgetProvider? Find(string widgetId) => registry.Find(widgetId);

    /// <summary>
    /// The name to show for a widget type right now. A type this build does not have
    /// answers with its own id, which makes a stale layout entry visible rather than
    /// blank.
    /// </summary>
    public string Name(string widgetId) =>
        Find(widgetId) is { } provider ? WidgetText.Value(provider.Descriptor.DisplayName) : widgetId;

    /// <summary>
    /// The observable behind a widget type's name, so a menu can be relettered in place
    /// instead of being rebuilt when the language changes. Null for a key this layer
    /// does not own, which is what a widget from a future plugin would hand over.
    /// </summary>
    public IObservable<string?>? ObservableName(string widgetId) =>
        Find(widgetId) is { } provider ? WidgetText.Observable(provider.Descriptor.DisplayName) : null;
}
