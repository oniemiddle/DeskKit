using DeskKit.App.Shell;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeskKit.App.Tests;

/// <summary>
/// The desktop gesture as the product applies it: what the preference turns on and
/// off, and what a double-click does. The recognition itself is the platform's and
/// is tested there; this is the command it ends up running.
/// </summary>
public sealed class DesktopGestureControllerTests
{
    [Fact]
    public void APreferenceThatIsOffLeavesTheDesktopUnwatched()
    {
        var gestures = new FakeGestures();

        NewController(new FakeShell(new AppSettings()), gestures).Start();

        Assert.Equal(0, gestures.StartCount);
        Assert.Equal(0, gestures.StopCount);
    }

    [Fact]
    public void APreferenceThatIsOnWatchesTheDesktop()
    {
        var gestures = new FakeGestures();

        NewController(new FakeShell(SwitchedOn()), gestures).Start();

        Assert.Equal(1, gestures.StartCount);
    }

    [Fact]
    public void TurningThePreferenceOffAndOnAgainFollowsIt()
    {
        var shell = new FakeShell(SwitchedOn());
        var gestures = new FakeGestures();
        var controller = NewController(shell, gestures);

        controller.Start();
        Assert.Equal(1, gestures.StartCount);

        shell.ApplySettings(shell.State.Settings with { DesktopDoubleClickTogglesWidgets = false });
        Assert.Equal(1, gestures.StopCount);
        Assert.Equal(1, gestures.StartCount);

        shell.ApplySettings(shell.State.Settings with { DesktopDoubleClickTogglesWidgets = true });
        Assert.Equal(2, gestures.StartCount);
    }

    [Fact]
    public void AChangeToAnyOtherPreferenceLeavesTheWatchAlone()
    {
        var shell = new FakeShell(SwitchedOn());
        var gestures = new FakeGestures();

        NewController(shell, gestures).Start();
        shell.ApplySettings(shell.State.Settings with { Theme = "Dark" });

        Assert.Equal(1, gestures.StartCount);
        Assert.Equal(0, gestures.StopCount);
    }

    [Fact]
    public void ADesktopDoubleClickRunsTheSameCommandAsTheTray()
    {
        var shell = new FakeShell(SwitchedOn());
        var gestures = new FakeGestures();

        NewController(shell, gestures).Start();

        gestures.RaiseDoubleClick();
        gestures.RaiseDoubleClick();

        // Hidden, then shown again: one command, stored once, and read back from the
        // shell each time rather than remembered here.
        Assert.Equal(new[] { false, true }, shell.VisibilityChanges);
        Assert.True(shell.State.Settings.WidgetsVisible);
    }

    [Fact]
    public void APlatformThatCannotWatchIsNotAFailure()
    {
        var gestures = new FakeGestures { IsSupported = false };
        var controller = NewController(new FakeShell(SwitchedOn()), gestures);

        // The preference stands and the command is still on the tray menu, so a
        // machine that refuses the hook has nothing else changed about it.
        controller.Start();

        Assert.Equal(1, gestures.StartCount);
    }

    [Fact]
    public void ADisposedControllerStopsWatchingAndStopsListening()
    {
        var shell = new FakeShell(SwitchedOn());
        var gestures = new FakeGestures();
        var controller = NewController(shell, gestures);

        controller.Start();
        controller.Dispose();

        gestures.RaiseDoubleClick();

        Assert.Equal(1, gestures.StopCount);
        Assert.Empty(shell.VisibilityChanges);
    }

    /// <summary>The preference that watches the desktop, with the widgets showing.</summary>
    private static AppSettings SwitchedOn() =>
        new() { WidgetsVisible = true, DesktopDoubleClickTogglesWidgets = true };

    private static DesktopGestureController NewController(FakeShell shell, FakeGestures gestures) =>
        new(shell, gestures, NullLogger<DesktopGestureController>.Instance);

    private sealed class FakeGestures : IDesktopGestureService
    {
        public bool IsSupported { get; set; } = true;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public event EventHandler? BackdropDoubleClicked;

        public void Start() => StartCount++;

        public void Stop() => StopCount++;

        public void RaiseDoubleClick() => BackdropDoubleClicked?.Invoke(this, EventArgs.Empty);
    }
}
