using DeskKit.App.ViewModels;
using DeskKit.App.Views;
using DeskKit.Core.Models;
using DeskKit.Runtime;

namespace DeskKit.App.Shell;

/// <summary>
/// The settings window: one instance, shown on demand and re-opened rather than
/// rebuilt.
/// </summary>
/// <remarks>
/// It is an ordinary top-level window, unlike the widgets, so it behaves the way any
/// other application window does. The view model reads the live shell state and writes
/// changes straight back, which is why the controller only has to own the window.
/// </remarks>
internal sealed class SettingsWindowController(IShellFacade shell, ShellAssets assets)
{
    private SettingsWindow? _window;

    /// <summary>
    /// Shows the window, optionally bringing one widget's row into view.
    /// </summary>
    /// <param name="widget">
    /// The widget whose settings to open, or null when the window was opened from the
    /// tray rather than from a widget.
    /// </param>
    public void Open(WidgetViewModel? widget)
    {
        _window ??= Create();

        if (!_window.IsVisible)
            _window.Show();

        _window.Activate();

        if (widget is not null)
            _window.SelectWidget(widget);
    }

    public void Close() => _window?.Close();

    private SettingsWindow Create()
    {
        var window = new SettingsWindow
        {
            Icon = assets.Icon,
            DataContext = new SettingsViewModel(shell),
        };

        // Closing must not end the process, and the next Open has to build a new one
        // rather than reuse a closed window.
        window.Closed += (_, _) => _window = null;
        return window;
    }
}
