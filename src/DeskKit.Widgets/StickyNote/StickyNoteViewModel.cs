using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskKit.Core.Models;

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
    private static readonly (string Name, Color Colour)[] Papers =
    [
        ("黄色", Color.Parse("#FFF3C4")),
        ("粉色", Color.Parse("#FFD6E4")),
        ("绿色", Color.Parse("#D7F2D0")),
        ("蓝色", Color.Parse("#D6E7FF")),
        ("紫色", Color.Parse("#E4DBFF")),
        ("灰色", Color.Parse("#E8E8E8")),
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
    }

    public IBrush PaperBrush => new SolidColorBrush(Papers[PaperIndex].Colour);

    public static IReadOnlyList<string> PaperNames => [.. Papers.Select(p => p.Name)];

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

    private void Persist<T>(string key, T value)
    {
        if (_loading)
            return;

        Settings.Set(key, value);
        Host.RequestSave();
    }
}
