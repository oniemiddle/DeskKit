using Avalonia.Media.Imaging;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Widgets.QuickLaunch;

namespace DeskKit.App.Tests;

/// <summary>
/// When the launcher asks for a shortcut's icon.
/// </summary>
/// <remarks>
/// A tile whose picture never arrives is a blank square with a name under it, and nothing
/// about that fails loudly: <see cref="WidgetViewModel.Start"/> makes exactly one pass over
/// the shortcuts that exist at that moment, so one added afterwards — dropped from
/// Explorer, which is the main way they get in — kept an empty picture for the rest of the
/// session. Both halves are pinned here, because the fix is only correct if it also leaves
/// the shortcuts that predate the pass to it.
/// </remarks>
public sealed class QuickLaunchIconTests
{
    private const string Notepad = @"C:\Windows\notepad.exe";

    [Fact]
    public void AShortcutAddedAfterStartAsksForItsIcon()
    {
        var loader = new RecordingIconLoader();
        var launcher = new QuickLaunchViewModel(Context(), loader);

        // The widget came up empty, so the single pass Start makes had nothing to do and
        // anything asked for from here on is the path under test.
        launcher.Start();
        loader.Requests.Clear();

        launcher.AddPaths([Notepad]);

        Assert.Equal([Notepad], loader.Requests);
    }

    [Fact]
    public void AShortcutAddedBeforeStartIsLeftToTheStartPass()
    {
        var loader = new RecordingIconLoader();
        var launcher = new QuickLaunchViewModel(Context(), loader);

        launcher.AddPaths([Notepad]);

        // Nothing is asked for while the widget is not on screen...
        Assert.Empty(loader.Requests);

        // ...and the pass Start makes picks it up, so deferring loses nothing.
        launcher.Start();

        Assert.Equal([Notepad], loader.Requests);
    }

    /// <summary>
    /// A launcher configured the way the shell configures one: its settings are the
    /// placement's own dictionary, and the host is only ever told that a save is due.
    /// </summary>
    private static WidgetContext Context()
    {
        var placement = new WidgetPlacement
        {
            InstanceId = "launcher-test",
            WidgetId = QuickLaunchWidgetProvider.WidgetId,
        };

        return new WidgetContext(placement, new WidgetSettings(placement.Settings), new SilentHost());
    }

    /// <summary>
    /// Records which paths were asked for and answers with no picture, which is all the
    /// question "was it asked for" needs.
    /// </summary>
    private sealed class RecordingIconLoader : IShellIconLoader
    {
        public List<string> Requests { get; } = [];

        public bool IsSupported => true;

        /// <summary>
        /// Answers with an already-completed task, so the caller's continuation runs
        /// inline and the assertion does not have to race a background one.
        /// </summary>
        public Task<Bitmap?> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            Requests.Add(path);

            return Task.FromResult<Bitmap?>(null);
        }
    }

    private sealed class SilentHost : IWidgetHost
    {
        public IWidgetMessageBus Messages { get; } = new WidgetMessageBus();

        public IReadOnlyList<ScreenBounds> Screens { get; } = [];

        public void ShowSettings(WidgetViewModel widget)
        {
        }

        public void RemoveWidget(WidgetViewModel widget)
        {
        }

        public void RequestSave()
        {
        }
    }
}
