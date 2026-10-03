using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Runtime;
using Microsoft.Extensions.Logging;

namespace DeskKit.App.Shell;

/// <summary>
/// Turns a gesture on the desktop itself into the product's command: a
/// double-click on an empty part of the desktop shows or hides every widget.
/// </summary>
/// <remarks>
/// It runs the same command as the tray's show/hide item rather than a second
/// copy of the idea, so the two can never disagree: the shell stores the
/// preference and raises the change, and the tray menu, the settings window and
/// this controller all read that one answer.
/// <para>
/// Watching is a preference rather than a fixture, because the double-click is one
/// Explorer offers for its own purpose ("double-click to show desktop icons"), so
/// the watch follows the setting: registered when the user asks for it, and
/// released when they turn it off.
/// </para>
/// </remarks>
internal sealed class DesktopGestureController(
    IShellFacade shell,
    IDesktopGestureService gestures,
    ILogger<DesktopGestureController> logger) : IDisposable
{
    private bool _started;
    private bool _watching;
    private bool _disposed;

    /// <summary>
    /// Starts following the stored preference. Called once the shell has started,
    /// because what it follows is the state the shell loaded.
    /// </summary>
    public void Start()
    {
        if (_started)
            return;

        _started = true;
        gestures.BackdropDoubleClicked += OnBackdropDoubleClicked;
        shell.StateChanged += OnShellStateChanged;

        Apply(shell.State.Settings);
    }

    /// <summary>Makes the watch match the preference it is handed.</summary>
    public void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.DesktopDoubleClickTogglesWidgets == _watching)
            return;

        _watching = settings.DesktopDoubleClickTogglesWidgets;

        if (!_watching)
        {
            gestures.Stop();
            return;
        }

        gestures.Start();

        if (!gestures.IsSupported)
        {
            // Refused by the machine — a hook the system will not register. The
            // preference stays as the user set it, and the command is still reachable
            // from the tray, so nothing else has to change on account of it.
            logger.LogWarning("The desktop double-click gesture is not available on this machine");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        gestures.BackdropDoubleClicked -= OnBackdropDoubleClicked;
        shell.StateChanged -= OnShellStateChanged;
        gestures.Stop();
    }

    private void OnBackdropDoubleClicked(object? sender, EventArgs e) =>
        shell.SetWidgetsVisible(!shell.State.Settings.WidgetsVisible);

    private void OnShellStateChanged(object? sender, EventArgs e) => Apply(shell.State.Settings);
}
