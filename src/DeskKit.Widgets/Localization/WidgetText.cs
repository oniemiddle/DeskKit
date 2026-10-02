using DeskKit.Core;

namespace DeskKit.Widgets.Localization;

/// <summary>
/// The bridge between a widget descriptor and its translated name.
/// <para>
/// A widget's name belongs to the widget, so the keys and the mapping to their
/// observables live here rather than in the shell. What the shell gets is a key it
/// carries in the descriptor like any other piece of metadata: the core model has
/// no opinion about languages, and does not take a dependency on the
/// localization library to hold one.
/// </para>
/// </summary>
public static class WidgetText
{
    public const string ClockName = "Widget_Clock_Name";
    public const string ClockDescription = "Widget_Clock_Description";
    public const string StickyNoteName = "Widget_StickyNote_Name";
    public const string StickyNoteDescription = "Widget_StickyNote_Description";
    public const string QuickLaunchName = "Widget_QuickLaunch_Name";
    public const string QuickLaunchDescription = "Widget_QuickLaunch_Description";

    public const string PaperYellow = "Note_Paper_Yellow";
    public const string PaperPink = "Note_Paper_Pink";
    public const string PaperGreen = "Note_Paper_Green";
    public const string PaperBlue = "Note_Paper_Blue";
    public const string PaperPurple = "Note_Paper_Purple";
    public const string PaperGrey = "Note_Paper_Grey";

    /// <summary>
    /// The observable behind a key, or null for a key this layer does not own —
    /// which is what a widget from a future plugin would hand over.
    /// </summary>
    /// <remarks>
    /// Asked of the library rather than mapped again here. The generated manager
    /// already holds a key-to-observable table, and a second copy of it would only be
    /// a place for a key added to the resource file to go missing.
    /// </remarks>
    public static IObservable<string?>? Observable(string key) =>
        WidgetLanguage.Instance.GetObservable(key);

    /// <summary>
    /// The key's value right now, for callers that only need a snapshot. Subscribe
    /// emits the current value immediately, so this needs no separate lookup; it
    /// returns the key itself when nothing owns it, which makes a missing
    /// translation visible instead of blank.
    /// </summary>
    public static string Value(string key)
    {
        if (Observable(key) is not { } observable)
            return key;

        var value = observable.CurrentValue();
        return string.IsNullOrEmpty(value) ? key : value;
    }
}
