namespace DeskKit.Core.Abstractions;

/// <summary>
/// Watches the desktop itself for the gestures the product answers: today, a
/// double-click on the wallpaper where no icon and no window is.
/// </summary>
/// <remarks>
/// The desktop belongs to Explorer, so no window of ours is in a position to
/// hear this: the pixels the user clicks are covered by the shell's own windows
/// (and, where desktop icons are on, by its icon list). The platform is
/// therefore responsible for noticing the gesture somewhere under the desktop's
/// own windows, and this contract is what it says when it has.
/// <para>
/// Where the decision is <em>made</em> is the product's: the runtime owns
/// windows and widgets and knows nothing about a mouse, and the app is what
/// turns the gesture into the same command the tray's "hide all widgets" uses.
/// </para>
/// <para>
/// Calls are expected from the UI thread. The event is raised on it whatever
/// thread the gesture was noticed on.
/// </para>
/// </remarks>
public interface IDesktopGestureService
{
    /// <summary>
    /// False where the desktop cannot be watched — a non-Windows build, or a
    /// Windows machine where the watch was refused. Callers then simply offer
    /// the command somewhere else.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Raised when the user double-clicked an empty part of the desktop: the
    /// wallpaper, not an icon, not a window, and not one of our own widgets.
    /// </summary>
    event EventHandler? BackdropDoubleClicked;

    /// <summary>
    /// Starts watching, and does nothing when already watching or unsupported.
    /// Safe to call again after <see cref="Stop"/>.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops watching and releases whatever the watch holds. Idempotent.
    /// </summary>
    void Stop();
}

/// <summary>
/// Fallback used where the desktop is not watched. An unsupported gesture is a
/// missing feature, never an error.
/// </summary>
public sealed class NullDesktopGestureService : IDesktopGestureService
{
    public bool IsSupported => false;

    public event EventHandler? BackdropDoubleClicked
    {
        add
        {
        }
        remove
        {
        }
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }
}
