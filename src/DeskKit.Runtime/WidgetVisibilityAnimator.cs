using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Runtime.Views;
using Microsoft.Extensions.Logging;

namespace DeskKit.Runtime;

/// <summary>
/// Slides the widget windows off their screen and back when they are hidden or
/// shown, so a tray command or a double-click on the desktop reads as the widgets
/// leaving rather than blinking out.
/// </summary>
/// <remarks>
/// The movement is of the window itself, not of the card inside it: a card
/// transformed within its own window would be clipped by that window and read as
/// a card sliding about inside a fixed frame. A window is positioned in physical
/// pixels, so every distance here is converted from the window's logical size.
/// <para>
/// Every widget in a run moves by the <em>same</em> vector, taken from the union of
/// where they all are, so the set slides as one thing. That is not a detail: an
/// offset worked out per widget would move each one at its own speed, and the ones
/// further along would be caught up by the ones behind them.
/// </para>
/// <para>
/// The pinning hook is suspended whenever a window is deliberately placed — the
/// reveal and the final put-back — because that is the shell putting a window
/// somewhere rather than a window misbehaving.
/// </para>
/// <para>
/// Frames come from the compositor, so a position update lands once per displayed
/// frame instead of on a timer that drifts against the refresh rate; the progress
/// itself comes from a clock, so a dropped frame skips a position rather than
/// stretching the slide. A slow watchdog finishes a run whose frames stopped
/// arriving, which is what keeps a window that was closed or hidden mid-slide from
/// leaving the widgets half-way out.
/// </para>
/// </remarks>
internal sealed class WidgetVisibilityAnimator(IDesktopLayerService desktopLayer, ILogger logger)
{
    /// <summary>
    /// How long the watchdog waits before finishing a slide whose frames stopped
    /// arriving. Generous, because it exists for windows that have gone away rather
    /// than to pace anything.
    /// </summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMilliseconds(250);

    private readonly Stopwatch _clock = new();

    private DispatcherTimer? _watchdog;
    private List<Frame> _frames = [];
    private bool _showing;
    private bool _frameRequested;
    private int _heldFrames;
    private string _easing = WidgetAnimationEasingSetting.Standard;
    private int _durationMs;

    /// <summary>True while a slide is in progress.</summary>
    public bool IsRunning => _frames.Count > 0;

    /// <summary>
    /// Shows or hides every widget, sliding when the preference asks for it and
    /// standing still when it does not.
    /// </summary>
    /// <param name="screens">
    /// The monitor layout, used to work out which edge a widget leaves by. Empty
    /// on a machine that cannot be asked, which only shortens the slide.
    /// </param>
    public void SetVisible(
        IReadOnlyList<WidgetRuntime> widgets,
        bool visible,
        IReadOnlyList<ScreenBounds> screens,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(widgets);
        ArgumentNullException.ThrowIfNull(settings);

        // Whatever was in flight is abandoned where it is; the run below starts from
        // where the windows actually are, so nothing teleports.
        var previous = Cancel();

        if (!WidgetAnimationSetting.IsAnimated(settings.WidgetsAnimation) || widgets.Count == 0)
        {
            // The preference changed while a slide was running, so anything it had
            // moved is put back before the widgets are simply shown or hidden.
            Settle(previous);
            ApplyImmediately(widgets, visible);
            return;
        }

        var duration = WidgetAnimationSpeedSetting.DurationMs(settings.WidgetAnimationSpeed);

        // Everything in the run moves by the same vector, worked out once from the
        // union of where the widgets are. Per-widget offsets would move each widget at
        // its own speed — the far ones barely budging while the near ones crossed the
        // screen — and the set would visibly come apart on the way out instead of
        // sliding as one.
        var bounds = UnionOf(widgets);
        var screen = screens.Count > 0
            ? PlacementNormalizer.FindNearestScreen(
                bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2), screens)
            : default;

        var offset = WidgetSlideAnimation.Offset(
            settings.WidgetAnimationDirection, bounds.X, bounds.Y, bounds.Width, bounds.Height, screen);

        var frames = new List<Frame>(widgets.Count);

        foreach (var widget in widgets)
        {
            var window = widget.Window;
            var position = window.Position;

            // A widget that is out of place is one the run above was moving, so the
            // place it belongs is the one that run was heading for — not where it
            // happens to be mid-slide. This is also what keeps a widget that the user
            // dragged a moment ago from being returned to where it used to be.
            var resting = Find(previous, widget)?.Resting ?? position;

            var displaced = new PixelPoint(resting.X + offset.X, resting.Y + offset.Y);

            frames.Add(visible
                ? new Frame(widget, resting, position == resting ? displaced : position, resting)
                : new Frame(widget, resting, position, displaced));
        }

        _frames = frames;
        _showing = visible;
        _easing = settings.WidgetAnimationEasing;
        _durationMs = Math.Max(1, duration);

        if (visible)
            RevealAtStart(frames);

        // A show holds for one frame before the clock starts, so the window it has
        // just revealed is on the screen at the start of the slide rather than
        // appearing part-way through it. Without the hold the first frame the user
        // sees is already a third of the way in — the show curve covers most of its
        // distance in the first few frames — and the slide reads as faster and
        // jerkier than the same slide started at rest. The reference implementation
        // makes the same distinction, waiting for its content to be ready before it
        // starts moving; a hidden window has nothing to reveal, so hiding is not held.
        _heldFrames = visible ? 1 : 0;

        _clock.Reset();
        StartWatchdog();
        RequestFrame();
    }

    /// <summary>Stops moving, leaving the windows wherever they are.</summary>
    public void Stop()
    {
        if (Cancel().Count > 0)
            logger.LogDebug("A widget slide was abandoned");
    }

    /// <summary>
    /// Forgets a widget that is being destroyed, so a frame that is still to come
    /// cannot touch a window that no longer exists.
    /// </summary>
    public void Forget(WidgetRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _frames.RemoveAll(frame => ReferenceEquals(frame.Widget, runtime));

        // The window that was driving the frames may be the one that just went away.
        _frameRequested = false;
    }

    public void Dispose()
    {
        Cancel();
        _watchdog?.Stop();
        _watchdog = null;
    }

    /// <summary>
    /// Asks the compositor for the next frame, so a position update lands once per
    /// displayed frame rather than on a timer that drifts against the refresh rate.
    /// </summary>
    /// <remarks>
    /// The window is invalidated first, and that is not decoration: Avalonia only
    /// produces a frame when something needs drawing, so a loop that merely requests
    /// one is answered at roughly half the refresh rate — measured at 35 frames a
    /// second against 59 with the invalidation, on a 60Hz display. Half the frames
    /// means every step is twice as large, and since the curve is steepest at the
    /// start it is the first step of a show that suffers most, which reads as the
    /// widget setting off too fast.
    /// <para>
    /// Only the window the frames are requested from is invalidated. The others are
    /// moved by the same callback and are composited by the system like any other
    /// window, so they do not each need a render pass of their own.
    /// </para>
    /// </remarks>
    private void RequestFrame()
    {
        if (_frameRequested || _frames.Count == 0)
            return;

        var window = _frames[0].Widget.Window;
        _frameRequested = true;

        try
        {
            window.InvalidateVisual();
            window.RequestAnimationFrame(_ =>
            {
                _frameRequested = false;
                OnFrame();
            });
        }
        catch (Exception ex)
        {
            // A window that cannot deliver frames is finished by the watchdog.
            _frameRequested = false;
            logger.LogDebug(ex, "The compositor would not schedule a slide frame");
        }
    }

    /// <summary>
    /// The safety net: a window that is hidden, occluded or closed mid-slide stops
    /// producing frames, and a slide that stopped being driven must still end up
    /// somewhere rather than leaving the widgets half-way out.
    /// </summary>
    private void StartWatchdog()
    {
        if (_watchdog is null)
        {
            _watchdog = new DispatcherTimer { Interval = WatchdogInterval };
            _watchdog.Tick += (_, _) => OnFrame();
        }

        _watchdog.Stop();
        _watchdog.Start();
    }

    /// <summary>
    /// Brings the widgets on screen at the far end of their slide, before the first
    /// frame moves them in.
    /// </summary>
    private void RevealAtStart(List<Frame> frames)
    {
        foreach (var frame in frames)
        {
            try
            {
                using (desktopLayer.SuspendPinning(frame.Widget.Window))
                {
                    frame.Widget.Window.Position = frame.From;
                    desktopLayer.SetVisible(frame.Widget.Window, true);
                }
            }
            catch (Exception ex)
            {
                // A window that cannot be shown is not a reason to strand the rest of
                // the widgets half-animated.
                logger.LogWarning(ex, "A widget could not be shown for the slide");
            }
        }
    }

    /// <summary>
    /// Puts back anything a run that has just been abandoned had moved, so a
    /// widget is never left halfway through a slide it is no longer taking part in.
    /// </summary>
    private void Settle(List<Frame> frames)
    {
        foreach (var frame in frames)
        {
            try
            {
                frame.Widget.Window.Position = frame.Resting;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A widget could not be put back after a slide");
            }
        }
    }

    private void ApplyImmediately(IReadOnlyList<WidgetRuntime> widgets, bool visible)
    {
        foreach (var widget in widgets)
        {
            try
            {
                desktopLayer.SetVisible(widget.Window, visible);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A widget could not be shown or hidden");
            }
        }
    }

    /// <summary>
    /// Stops the run in progress and answers with the frames it was working
    /// through, which is what a run that replaces it needs to know about where each
    /// widget belongs.
    /// </summary>
    private List<Frame> Cancel()
    {
        var frames = _frames;

        _watchdog?.Stop();
        _clock.Reset();
        _frames = [];
        _frameRequested = false;

        return frames;
    }

    private void OnFrame()
    {
        var frames = _frames;
        if (frames.Count == 0)
        {
            Cancel();
            return;
        }

        // The slide begins on the first frame the window is actually on the screen,
        // which is the frame after a reveal.
        if (_heldFrames > 0)
        {
            _heldFrames--;
            Apply(frames, 0);

            if (_heldFrames == 0)
                RequestFrame();

            return;
        }

        if (!_clock.IsRunning)
            _clock.Start();

        var progress = Math.Clamp(_clock.Elapsed.TotalMilliseconds / _durationMs, 0, 1);
        Apply(frames, progress);

        if (progress >= 1)
        {
            Complete();
            return;
        }

        RequestFrame();
    }

    private void Apply(List<Frame> frames, double progress)
    {
        foreach (var frame in frames)
        {
            try
            {
                frame.Widget.Window.Position = frame.PositionAt(_easing, progress, _showing);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A widget could not be moved during a slide");
            }
        }
    }

    /// <summary>
    /// Puts every widget where the run was heading and leaves it there: shown and
    /// pinned, or hidden with its window back in its own place.
    /// </summary>
    private void Complete()
    {
        var frames = Cancel();

        foreach (var frame in frames)
        {
            var window = frame.Widget.Window;

            try
            {
                if (_showing)
                {
                    window.Position = frame.Resting;
                    desktopLayer.Reassert(window);
                    continue;
                }

                // Hidden from where the slide ended, then put back: a hidden widget
                // that is left off the screen would appear there the next time
                // anything else showed it. The move happens while the window is
                // hidden, so there is nothing to see.
                using (desktopLayer.SuspendPinning(window))
                {
                    desktopLayer.SetVisible(window, false);
                    window.Position = frame.Resting;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A widget could not be left where its slide ended");
            }
        }
    }

    /// <summary>
    /// The rectangle containing every widget in the run, in physical pixels: what
    /// moves, as one thing, when the set is shown or hidden.
    /// </summary>
    private static (int X, int Y, int Width, int Height) UnionOf(IReadOnlyList<WidgetRuntime> widgets)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;

        foreach (var widget in widgets)
        {
            var position = widget.Window.Position;
            var size = PhysicalSizeOf(widget.Window);

            left = Math.Min(left, position.X);
            top = Math.Min(top, position.Y);
            right = Math.Max(right, position.X + size.Width);
            bottom = Math.Max(bottom, position.Y + size.Height);
        }

        if (left == int.MaxValue)
            return (0, 0, 0, 0);

        return (left, top, right - left, bottom - top);
    }

    private static Frame? Find(List<Frame> frames, WidgetRuntime widget)
    {
        foreach (var frame in frames)
        {
            if (ReferenceEquals(frame.Widget, widget))
                return frame;
        }

        return null;
    }

    /// <summary>The window's own size in physical pixels, which is the space it moves in.</summary>
    private static PixelSize PhysicalSizeOf(WidgetWindow window)
    {
        var scaling = window.RenderScaling;
        if (scaling <= 0)
            scaling = 1;

        var size = window.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            size = new Size(window.Width, window.Height);

        return new PixelSize(
            (int)Math.Round(size.Width * scaling),
            (int)Math.Round(size.Height * scaling));
    }

    /// <summary>One widget's part in a slide.</summary>
    /// <param name="Resting">Where the widget belongs, off any slide.</param>
    private sealed record Frame(WidgetRuntime Widget, PixelPoint Resting, PixelPoint From, PixelPoint To)
    {
        public PixelPoint PositionAt(string easing, double progress, bool showing) =>
            new(
                From.X + WidgetSlideAnimation.Distance(easing, To.X - From.X, progress, showing),
                From.Y + WidgetSlideAnimation.Distance(easing, To.Y - From.Y, progress, showing));
    }
}
