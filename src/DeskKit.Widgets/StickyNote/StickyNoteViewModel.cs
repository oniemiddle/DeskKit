using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskKit.Core;
using DeskKit.Core.Models;
using DeskKit.Widgets.Localization;

namespace DeskKit.Widgets.StickyNote;

/// <summary>
/// A single note. Every keystroke updates the widget's stored settings; the
/// shell coalesces those into one debounced file write, so typing stays cheap.
/// </summary>
public sealed partial class StickyNoteViewModel : WidgetViewModel
{
    private const string KeyTitle = "title";
    private const string KeyBody = "body";
    private const string KeyPaper = "paper";
    private const string KeyFontSize = "fontSize";

    /// <summary>Paper tints, all light enough for dark text.</summary>
    private static readonly (string NameKey, Color Colour)[] Papers =
    [
        (WidgetText.PaperYellow, Color.Parse("#FFF3C4")),
        (WidgetText.PaperPink, Color.Parse("#FFD6E4")),
        (WidgetText.PaperGreen, Color.Parse("#D7F2D0")),
        (WidgetText.PaperBlue, Color.Parse("#D6E7FF")),
        (WidgetText.PaperPurple, Color.Parse("#E4DBFF")),
        (WidgetText.PaperGrey, Color.Parse("#E8E8E8")),
    ];

    private bool _loading = true;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private int _paperIndex;

    [ObservableProperty]
    private double _noteFontSize = 14;

    public StickyNoteViewModel(WidgetContext context)
        : base(context)
    {
        Title = Settings.Get(KeyTitle, string.Empty);
        Body = Settings.Get(KeyBody, string.Empty);
        PaperIndex = Math.Clamp(Settings.Get(KeyPaper, 0), 0, Papers.Length - 1);
        NoteFontSize = Math.Clamp(Settings.Get(KeyFontSize, 14d), 10, 32);

        _loading = false;

        // The palette is the one piece of this widget's own text that is not in
        // XAML, so it has to refresh itself.
        _cultureSubscription = WidgetLanguage.Instance.CultureChanges
            .SubscribeAction(_ => OnPropertyChanged(nameof(PaperNames)));
    }

    private readonly IDisposable _cultureSubscription;

    public IBrush PaperBrush => new SolidColorBrush(Papers[PaperIndex].Colour);

    /// <summary>
    /// The palette names in the active language. Re-read on every culture change,
    /// because the list is shown in the settings panel and would otherwise stay in
    /// the language the note was created in.
    /// </summary>
    public IReadOnlyList<string> PaperNames => [.. Papers.Select(p => WidgetText.Value(p.NameKey))];

    public override Control CreateView() => new StickyNoteView { DataContext = this };

    public override Control? CreateSettingsView() => new StickyNoteSettingsView { DataContext = this };

    public void SelectPaper(int index)
    {
        PaperIndex = Math.Clamp(index, 0, Papers.Length - 1);
    }

    partial void OnTitleChanged(string value) => Persist(KeyTitle, value);

    partial void OnBodyChanged(string value) => Persist(KeyBody, value);

    partial void OnNoteFontSizeChanged(double value) => Persist(KeyFontSize, Math.Round(value));

    partial void OnPaperIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PaperBrush));
        Persist(KeyPaper, value);
    }

    /// <summary>Releases the culture subscription so a removed note stops listening.</summary>
    public override void Dispose() => _cultureSubscription.Dispose();

    private void Persist<T>(string key, T value)
    {
        if (_loading)
            return;

        Settings.Set(key, value);
        Host.RequestSave();
    }
}
