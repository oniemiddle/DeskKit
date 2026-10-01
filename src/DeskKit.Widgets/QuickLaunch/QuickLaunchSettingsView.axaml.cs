using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

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

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "添加快捷方式",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("程序与快捷方式") { Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd"] },
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
