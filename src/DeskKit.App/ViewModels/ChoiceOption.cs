using CommunityToolkit.Mvvm.ComponentModel;

namespace DeskKit.App.ViewModels;

/// <summary>
/// A choice in a settings drop-down: a stored key with the text shown for it.
/// <para>
/// The key is what gets saved, and the label is only ever shown. Matching a
/// selection back from its label — which this used to do — cannot survive the
/// labels being translated, because the saved value would then depend on the
/// language that was active when it was picked.
/// </para>
/// </summary>
public sealed partial class ChoiceOption(string key) : ObservableObject
{
    public string Key { get; } = key;

    [ObservableProperty]
    private string _label = key;

    public override string ToString() => Label;
}
