using DeskKit.Core.Abstractions;
using DeskKit.Core.Services;
using DeskKit.Widgets.Localization;
using DeskKit.Runtime;

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
public sealed class WidgetCatalog(WidgetRegistry registry)
{
    /// <summary>The widget types, in the order they should be offered.</summary>
    public IReadOnlyList<IWidgetProvider> Providers => registry.Providers;

    public IWidgetProvider? Find(string widgetId) => registry.Find(widgetId);

    /// <summary>
    /// One placed widget as the product shows it: its identity, and the name to display
    /// for it in the culture in force right now.
    /// </summary>
    /// <remarks>
    /// The name is resolved when this is asked for rather than carried on the placement,
    /// which only holds the widget's id. The settings window re-reads its list whenever
    /// the culture changes, so the names follow the language.
    /// </remarks>
    public WidgetInfo Describe(WidgetRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        return new WidgetInfo(
            runtime.Placement.InstanceId,
            runtime.Placement.WidgetId,
            Name(runtime.Placement.WidgetId),
            runtime.ViewModel);
    }

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
