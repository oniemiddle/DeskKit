using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace DeskKit.Widgets.QuickLaunch;

/// <summary>
/// The launcher's on-desktop appearance. It accepts files dropped from Explorer,
/// which is the main way shortcuts get in.
/// </summary>
public partial class QuickLaunchView : UserControl
{
    public QuickLaunchView()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not QuickLaunchView { DataContext: QuickLaunchViewModel viewModel })
            return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return;

        viewModel.AddPaths(files
            .Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path)));

        e.Handled = true;
    }
}
