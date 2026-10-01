using Avalonia.Controls;

namespace DeskKit.Platform;

/// <summary>Per-window desktop-layer behaviour.</summary>
/// <param name="PreventActivation">
/// When true the widget is given <c>WS_EX_NOACTIVATE</c>, so clicking it does
/// not take focus away from the application the user is working in. Widgets
/// that need keyboard input (the sticky note) must leave this false.
/// </param>
/// <param name="ForceBottom">Keep forcing the window to the bottom of the z-order.</param>
/// <param name="RejectHide">Refuse requests that would hide the window.</param>
public sealed record DesktopLayerOptions(
    bool PreventActivation,
    bool ForceBottom = true,
    bool RejectHide = true);

/// <summary>
/// Keeps widget windows glued to the desktop: above the wallpaper, below every
/// ordinary window, and unaffected by "Show Desktop".
/// </summary>
public interface IDesktopLayerService
{
    /// <summary>
    /// False on platforms where desktop-layer pinning is not implemented; the
    /// caller then falls back to an ordinary window.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Starts pinning <paramref name="window"/>. Must be called after the
    /// window has a platform handle, i.e. once it has been shown.
    /// </summary>
    void Attach(Window window, DesktopLayerOptions options);

    /// <summary>Stops pinning and restores the window's normal styles.</summary>
    void Detach(Window window);

    /// <summary>
    /// Pushes the window back to the bottom of the z-order. Cheap and safe to
    /// call often; it is a no-op when the window is already there.
    /// </summary>
    void Reassert(Window window);

    /// <summary>
    /// Records the window's current size as the "normal" size, so a later
    /// collapse to the caption icon rect can be recognised and rejected.
    /// Call after the user finishes resizing.
    /// </summary>
    void SyncNormalSize(Window window);

    /// <summary>
    /// Shows or hides the window with the pinning hook suspended, so an
    /// intentional hide is not immediately undone.
    /// </summary>
    void SetVisible(Window window, bool visible);

    /// <summary>
    /// Suspends pinning until the returned scope is disposed, so the window can
    /// deliberately be something other than a bottom-most desktop window — for
    /// example while it is being hidden, moved to another monitor, or
    /// photographed by a diagnostic. Callers must call
    /// <see cref="Reassert"/> afterwards to restore the z-order.
    /// </summary>
    IDisposable SuspendPinning(Window window);
}
