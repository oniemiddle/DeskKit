namespace DeskKit.Core.Models;

/// <summary>
/// The arithmetic of a slide: how far a widget travels, and how the travel is
/// spread over the time it takes.
/// </summary>
/// <remarks>
/// Pure, like the placement and pinning rules, so the shape of the motion can be
/// pinned by unit tests instead of being judged by eye on one machine. The
/// animator that drives the frames owns nothing but a timer.
/// <para>
/// Every distance here is physical pixels, because that is the space a window is
/// positioned in; the caller converts from the window's own (logical) size.
/// </para>
/// </remarks>
public static class WidgetSlideAnimation
{
    /// <summary>
    /// How far something moves to leave its screen by <paramref name="direction"/>,
    /// and therefore where it comes back from.
    /// </summary>
    /// <param name="boundsX">The left edge of what is moving.</param>
    /// <param name="boundsY">The top edge of what is moving.</param>
    /// <param name="boundsWidth">Its width.</param>
    /// <param name="boundsHeight">Its height.</param>
    /// <remarks>
    /// What is moving is usually <em>everything at once</em>, so the bounds are the
    /// union of every widget in the run rather than one widget's rectangle. That is
    /// the whole reason this is a separate function: a set that slid by one vector
    /// per member would move each member at its own speed — the far ones barely
    /// moving while the near ones crossed the screen — and the members would visibly
    /// catch up with each other on the way out.
    /// <para>
    /// The travel is measured to the edge of the screen, not to a fixed offset:
    /// a set that slid only its own size would still be on screen when the windows
    /// are hidden, so the last frame of a hide would be widgets blinking out of
    /// existence rather than leaving. A set already at the edge it is leaving by
    /// would have nothing to travel, so every direction also guarantees at least the
    /// set's own size.
    /// </para>
    /// <para>
    /// The screen is the one the set is on, so on a multi-monitor desktop a slide
    /// can briefly cross onto a neighbouring monitor, the way it does in the
    /// reference implementation. The alternative — travelling to the edge of the
    /// whole virtual desktop — would send a set on an inner monitor flying across
    /// its neighbours.
    /// </para>
    /// </remarks>
    public static (int X, int Y) Offset(
        string? direction,
        int boundsX,
        int boundsY,
        int boundsWidth,
        int boundsHeight,
        ScreenBounds screen)
    {
        var normalized = WidgetAnimationDirectionSetting.Normalize(direction);
        var width = Math.Max(1, boundsWidth);
        var height = Math.Max(1, boundsHeight);

        if (screen.IsEmpty)
        {
            // No screen to leave: the set's own size is the only sensible travel.
            return normalized switch
            {
                WidgetAnimationDirectionSetting.Left => (-width, 0),
                WidgetAnimationDirectionSetting.Up => (0, -height),
                WidgetAnimationDirectionSetting.Down => (0, height),
                _ => (width, 0),
            };
        }

        return normalized switch
        {
            // The set's left edge ends up on the screen's right edge, which is the
            // first position at which none of it is still on the screen.
            WidgetAnimationDirectionSetting.Left => (Math.Min(screen.X - (boundsX + width), -width), 0),

            WidgetAnimationDirectionSetting.Up => (0, Math.Min(screen.Y - (boundsY + height), -height)),

            WidgetAnimationDirectionSetting.Down => (0, Math.Max(screen.Bottom - boundsY, height)),

            _ => (Math.Max(screen.Right - boundsX, width), 0),
        };
    }

    /// <summary>
    /// How much of the travel is behind the widget at <paramref name="progress"/>
    /// of the way through the time it takes: 0 at the start, 1 at the end.
    /// </summary>
    /// <param name="showing">
    /// Which way the widget is going, because the two directions do not want the
    /// same curve. A widget arriving should be <em>decelerating</em> — it is placed
    /// and settles — while one leaving should be <em>accelerating</em>, having been
    /// still a moment earlier. Using one curve for both is what makes a slide read
    /// as a jump followed by a crawl in whichever direction it does not suit.
    /// </param>
    /// <remarks>
    /// The control points are the reference implementation's, which is where the
    /// shape comes from: a show side that overshoots slightly and settles, and a
    /// hide side that gathers itself before leaving. The middle of a run may
    /// therefore go past 1 — that is the overshoot, not an error — but the ends are
    /// exactly 0 and 1, so a widget still lands exactly where it belongs.
    /// </remarks>
    public static double Progress(string? easing, double progress, bool showing)
    {
        var clamped = Math.Clamp(progress, 0, 1);

        return WidgetAnimationEasingSetting.Normalize(easing) switch
        {
            WidgetAnimationEasingSetting.None => clamped,

            WidgetAnimationEasingSetting.Light => showing
                ? Bezier(0.25, 0.9, 0.25, 1.0, clamped)
                : Bezier(0.6, 0.1, 0.9, 0.3, clamped),

            WidgetAnimationEasingSetting.Strong => showing
                ? Bezier(0.05, 1.1, 0.15, 1.0, clamped)
                : Bezier(0.7, 0.0, 0.95, -0.1, clamped),

            WidgetAnimationEasingSetting.Standard => showing
                ? Bezier(0.16, 1.0, 0.3, 1.0, clamped)
                : Bezier(0.7, 0.0, 0.84, 0.0, clamped),

            // Unreachable while Normalize maps anything unknown to Standard, and
            // written as Standard's shape so it stays that way if that ever changes.
            _ => showing
                ? Bezier(0.16, 1.0, 0.3, 1.0, clamped)
                : Bezier(0.7, 0.0, 0.84, 0.0, clamped),
        };
    }

    /// <summary>How many pixels of the travel are behind the widget at that moment.</summary>
    public static int Distance(string? easing, int totalDistance, double progress, bool showing) =>
        (int)Math.Round(totalDistance * Progress(easing, progress, showing));

    /// <summary>
    /// Evaluates a cubic Bézier whose endpoints are (0,0) and (1,1), given the
    /// fraction of the way across in <paramref name="x"/> rather than the curve's
    /// own parameter.
    /// </summary>
    /// <remarks>
    /// A cubic Bézier is defined over its parameter, and what a caller has is a
    /// fraction of the elapsed time — that is, a value of <c>x</c>. Inverting
    /// x(u) needs a solver: Newton converges in a few steps for these curves, and
    /// bisection takes over when it does not, so a curve with extreme control
    /// points cannot produce a wrong answer or a NaN.
    /// <para>
    /// The result is deliberately not squashed into 0..1: a curve whose control
    /// points sit beyond the ends is asking for the widget to go slightly past
    /// where it is heading and come back, and clamping here would throw that away.
    /// </para>
    /// </remarks>
    private static double Bezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0)
            return 0;

        if (x >= 1)
            return 1;

        var u = x;

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var error = Curve(u, x1, x2) - x;
            if (Math.Abs(error) < Epsilon)
                return Curve(u, y1, y2);

            var slope = Slope(u, x1, x2);
            if (Math.Abs(slope) < Epsilon)
                break;

            u -= error / slope;
        }

        double low = 0, high = 1, result = x;

        for (var iteration = 0; iteration < 32; iteration++)
        {
            var value = Curve(result, x1, x2);
            if (Math.Abs(value - x) < Epsilon)
                break;

            if (value > x)
                high = result;
            else
                low = result;

            result = (low + high) / 2;
        }

        return Curve(result, y1, y2);
    }

    /// <summary>One coordinate of a Bézier from (0,0) to (1,1) through two control points.</summary>
    private static double Curve(double u, double p1, double p2)
    {
        var c = 3 * p1;
        var b = (3 * (p2 - p1)) - c;
        var a = 1 - c - b;

        return (((a * u) + b) * u + c) * u;
    }

    private static double Slope(double u, double p1, double p2)
    {
        var c = 3 * p1;
        var b = (3 * (p2 - p1)) - c;
        var a = 1 - c - b;

        return (((3 * a) * u) + (2 * b)) * u + c;
    }

    private const double Epsilon = 1e-6;
}
