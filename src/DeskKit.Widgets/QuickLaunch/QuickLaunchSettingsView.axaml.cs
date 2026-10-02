using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeskKit.Core;
using DeskKit.Widgets.Localization;

namespace DeskKit.Widgets.QuickLaunch;

/// <summary>The launcher's settings panel, hosted by the settings window.</summary>
public partial class QuickLaunchSettingsView : UserControl
{
    public QuickLaunchSettingsView() => InitializeComponent();

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not QuickLaunchViewModel viewModel)
            return;

        // The settings window is an ordinary window, so it can own a file dialog.
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        // Read at the moment the dialog opens rather than bound once, so the
        // dialog is in whatever language is active when it is actually shown.
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = WidgetLanguage.Instance.Launch_AddShortcutTitle.CurrentValue(),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(WidgetLanguage.Instance.Launch_ProgramsFilter.CurrentValue())
                {
                    Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd"],
                },
                FilePickerFileTypes.All,
            ],
        });

        viewModel.AddPaths(files.Select(file => file.Path.LocalPath));
    }

    private void OnAddUrlClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not QuickLaunchViewModel viewModel)
            return;

        viewModel.AddUrl(UrlBox.Text ?? string.Empty);
        UrlBox.Text = string.Empty;
    }
}
