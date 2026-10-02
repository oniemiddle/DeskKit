using CommunityToolkit.Mvvm.ComponentModel;

namespace DeskKit.App.ViewModels;

/// <summary>
/// Entry in the "add widget" picker.
/// <para>
/// The name is a resource key resolved by the widget layer, and the label is
/// observable so the picker can be relettered in place when the language changes.
/// The entry itself is kept rather than rebuilt, because the list is a combo box's
/// item source and its selection is the entry.
/// </para>
/// </summary>
public sealed partial class WidgetOption(string widgetId, string nameKey) : ObservableObject
{
    public string WidgetId { get; } = widgetId;

    public string NameKey { get; } = nameKey;

    [ObservableProperty]
    private string _displayName = nameKey;
}
