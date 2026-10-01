using Avalonia.Controls;
using DeskKit.App.ViewModels;
using DeskKit.Core.Models;

namespace DeskKit.App.Views;

/// <summary>
/// The application's settings window. It is an ordinary top-level window, unlike
/// the widgets, so it behaves exactly like any other app window.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    /// <summary>Brings the settings window to the given widget's row.</summary>
    public void SelectWidget(WidgetViewModel widget)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.SelectByInstanceId(widget.InstanceId);
    }
}
