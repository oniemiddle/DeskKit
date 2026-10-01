using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DeskKit.Core.Models;

namespace DeskKit.App.Views;

/// <summary>
/// Draws the magnetism glow on the card's own surface.
/// <para>
/// This is the same idea as the halo a card shows in a web UI when the pointer
/// comes near it — a soft light that answers to something touching the card —
/// except that here the thing it answers to is a neighbouring widget rather than
/// the cursor. Like that halo it lives entirely on the card: it is clipped to the
/// card's rounded outline and never reaches past it, so the widget itself is what
/// lights up.
/// </para>
/// <para>
/// It is hand-drawn rather than assembled from borders because the shape is not
/// expressible that way. The light comes from the region the two widgets share,
/// so on a vertical edge the intensity peaks along the shared stretch and decays
/// both inwards across the card and along the edge past the ends of that stretch.
/// A narrow specular band sits on the outermost pixels, standing in for the
/// reflection a lit edge would show.
/// One brush cannot fade along two axes, and a border carries one brush.
/// </para>
/// <para>
/// Segments arrive as fractions of the card, so the glow stays correct when the
/// widget is resized. It is never hit-testable — it is decoration.
/// </para>
/// </summary>
public sealed class WidgetGlowLayer : Control
{
    /// <summary>
    /// How far the glow reaches inwards from the shared edge when it runs
    /// vertically, in DIPs. Such a stretch fades across the card's width, which
    /// is its long side, so it needs more room than a horizontal one before it
    /// looks equally soft.
    /// </summary>
    public const double DefaultHorizontalFadeLength = 30;

    /// <summary>How far the glow reaches inwards from the shared edge when it runs horizontally, in DIPs.</summary>
    public const double DefaultVerticalFadeLength = 18;

    /// <summary>
    /// How far the light reaches along the edge beyond the region the two widgets
    /// share, in DIPs.
    /// <para>
    /// The shared stretch is where the light comes from, not how far it reaches.
    /// Without this the glow stops dead at the ends of the shared region, which
    /// reads as a painted rectangle rather than as light.
    /// </para>
    /// </summary>
    public const double DefaultSpreadAlongEdge = 28;

    /// <summary>
    /// How wide the bright band along the outermost edge is, in DIPs.
    /// <para>
    /// A lit edge in the real world shows a specular reflection: whatever surface
    /// is nearest the light turns almost white and the falloff happens just
    /// behind it. Without this the glow's brightest point is still a tint of the
    /// accent colour, which reads as a tinted panel rather than as light arriving
    /// from somewhere. The band is kept to a couple of DIPs because it stands in
    /// for a reflection, and a reflection is a line, not a region.
    /// </para>
    /// </summary>
    public const double DefaultEdgeHighlightWidth = 1.5;

    /// <summary>
    /// Opacity of that band where it is brightest. It is deliberately higher than
    /// the glow's own edge alpha, because the point of it is to be the brightest
    /// thing on the card.
    /// </summary>
    public const double DefaultEdgeHighlightOpacity = 0.75;

    /// <summary>How many steps are used to approximate the falloff curves.</summary>
    private const int CurveSteps = 16;

    /// <summary>
    /// How many sub-bands a corner band is split into. The falloff across a
    /// straight edge comes from a gradient, which cannot bend around a curve, so
    /// the corner is built from concentric sub-bands instead and each is given the
    /// intensity the gradient would have had at its middle. Four of them over
    /// `EdgeHighlightWidth` puts each step well under a pixel, where antialiasing
    /// blends them back into a ramp.
    /// </summary>
    private const int CornerBands = 4;

    /// <summary>How many segments each corner arc is sampled with.</summary>
    private const int CornerSteps = 8;

    private IReadOnlyList<WidgetGlowSegment> _segments = [];

    /// <summary>The stretches to draw. Setting this redraws the layer.</summary>
    public IReadOnlyList<WidgetGlowSegment> Segments
    {
        get => _segments;
        set
        {
            _segments = value;
            IsVisible = _segments.Count > 0;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Colour of the glow. It is the theme accent rather than the card colour, so
    /// the glow reads the same on a light and a dark widget.
    /// </summary>
    public Color GlowColor { get; set; } = Color.Parse("#FF5B8DEF");

    /// <summary>
    /// Opacity where the light is at its strongest, on the shared edge itself.
    /// Deliberately well below fully opaque: a solid colour reads as a painted
    /// stripe rather than as light falling on the surface.
    /// </summary>
    public double EdgeOpacity { get; set; } = 0.6;

    /// <summary>
    /// Shape of the falloff. Light decays faster near its source, so a linear
    /// ramp looks like a wedge; an exponent above 1 drops quickly at the source
    /// and then tapers, which reads as a more natural spill. It is applied to
    /// both the fade into the card and the reach along the edge, so the two
    /// directions agree.
    /// </summary>
    public double FalloffExponent { get; set; } = 1.5;

    /// <summary>Reach of the glow inwards from a vertical edge, in DIPs.</summary>
    public double HorizontalFadeLength { get; set; } = DefaultHorizontalFadeLength;

    /// <summary>Reach of the glow inwards from a horizontal edge, in DIPs.</summary>
    public double VerticalFadeLength { get; set; } = DefaultVerticalFadeLength;

    /// <summary>How far the light reaches along the edge beyond the shared region, in DIPs.</summary>
    public double SpreadAlongEdge { get; set; } = DefaultSpreadAlongEdge;

    /// <summary>
    /// Colour of the specular band on the edge. It is a light tint of the accent
    /// rather than pure white, so the highlight still belongs to the same light as
    /// the glow underneath it instead of looking like a separate white stroke.
    /// </summary>
    public Color HighlightColor { get; set; } = Color.Parse("#FFEAF2FF");

    /// <summary>Width of the specular band on the outermost edge, in DIPs.</summary>
    public double EdgeHighlightWidth { get; set; } = DefaultEdgeHighlightWidth;

    /// <summary>Opacity of the specular band where it is brightest.</summary>
    public double EdgeHighlightOpacity { get; set; } = DefaultEdgeHighlightOpacity;

    /// <summary>
    /// The card's corner radius. The glow is clipped to the card's outline, so
    /// it stops at the rounded corners rather than squaring off across them.
    /// </summary>
    public double CardCornerRadius { get; set; } = 14;

    /// <summary>Alpha actually used where the light is strongest, after clamping.</summary>
    public byte EdgeAlpha =>
        (byte)Math.Round(255 * Math.Clamp(EdgeOpacity, 0, 1));

    /// <summary>Alpha actually used for the specular band, after clamping.</summary>
    public byte HighlightAlpha =>
        (byte)Math.Round(255 * Math.Clamp(EdgeHighlightOpacity, 0, 1));

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_segments.Count == 0)
            return;

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        // The glow belongs to the card's surface, so nothing may escape it. The
        // card's own outline is the clip, which is also what keeps the reach
        // along the edge from running off the corners.
        using (context.PushGeometryClip(BuildCardClip(size)))
        {
            // A corner can be reached from either of the two edges that meet
            // there, and on a diagonal placement both do. It is a quarter circle,
            // not a sum of two lights, so it is drawn once.
            HashSet<WidgetCorner> cornersDrawn = [];

            foreach (var segment in _segments)
            {
                switch (segment.Edge)
                {
                    case WidgetEdge.Left:
                        DrawVertical(context, size, segment, fromRight: false, cornersDrawn);
                        break;

                    case WidgetEdge.Right:
                        DrawVertical(context, size, segment, fromRight: true, cornersDrawn);
                        break;

                    case WidgetEdge.Top:
                        DrawHorizontal(context, size, segment, fromBottom: false, cornersDrawn);
                        break;

                    case WidgetEdge.Bottom:
                        DrawHorizontal(context, size, segment, fromBottom: true, cornersDrawn);
                        break;
                }
            }
        }
    }

    /// <summary>The card's own outline, which is the whole area the glow may use.</summary>
    private RectangleGeometry BuildCardClip(Size size)
    {
        var radius = CornerRadiusFor(size);

        return new RectangleGeometry(new Rect(0, 0, size.Width, size.Height))
        {
            RadiusX = radius,
            RadiusY = radius,
        };
    }

    /// <summary>
    /// The card's corner radius, clamped to what actually fits. A radius larger
    /// than half the card is clamped by the geometry anyway, so the band has to
    /// clamp the same way or it would not follow the outline it is imitating.
    /// </summary>
    private double CornerRadiusFor(Size size) =>
        Math.Min(Math.Max(CardCornerRadius, 0), Math.Min(size.Width, size.Height) / 2);

    /// <summary>Draws a stretch along a vertical edge.</summary>
    private void DrawVertical(
        DrawingContext context,
        Size size,
        WidgetGlowSegment segment,
        bool fromRight,
        HashSet<WidgetCorner> cornersDrawn)
    {
        var fade = Math.Clamp(HorizontalFadeLength, 0, size.Width);
        if (fade <= 0)
            return;

        var shared = segment.Length * size.Height;
        if (shared <= 0)
            return;

        var spread = Math.Max(SpreadAlongEdge, 0);

        // The band reaches inwards only, so it stays on the card.
        var x = fromRight ? size.Width - fade : 0;
        var y = (segment.Start * size.Height) - spread;
        var area = new Rect(x, y, fade, shared + (spread * 2));

        var across = BuildAcrossFade(
            startX: fromRight ? 1 : 0, startY: 0,
            endX: fromRight ? 0 : 1, endY: 0);

        var mask = BuildAlongMask(shared, spread, vertical: true);

        // A corner arc reaches further inwards than the straight band, so the mask
        // rectangle has to be wide enough to cover it. Only the across axis is
        // widened, which leaves the along-edge gradient it defines untouched.
        var maskBounds = new Rect(0, y, size.Width, area.Height);

        var radius = CornerRadiusFor(size);
        var reachesTop = area.Top <= 0;
        var reachesBottom = area.Bottom >= size.Height;

        // The straight part stops where the outline begins to curve and the corner
        // pass takes over. Letting the two overlap would blend the band with itself
        // there and leave a brighter step at the seam.
        var alongTop = reachesTop ? Math.Min(radius, area.Bottom) : area.Top;
        var alongBottom = reachesBottom
            ? Math.Max(size.Height - radius, alongTop)
            : area.Bottom;
        var band = new Rect(x, alongTop, fade, alongBottom - alongTop);

        // The glow itself is not trimmed: it is a wash over the card's surface and
        // the card's own outline already bounds it, so it should keep flowing into
        // the corner under the band. Only the band is cut back, because that is the
        // part a second pass would otherwise draw on top of itself.
        using (context.PushOpacityMask(mask, maskBounds))
        {
            context.FillRectangle(across, area);
        }

        DrawEdgeHighlight(context, mask, maskBounds, band, fade, vertical: true, fromEnd: fromRight);

        // A corner is only lit when the light actually reaches it; a shared stretch
        // that stops short of the end of the edge has nothing to reflect off it.
        var width = Math.Clamp(EdgeHighlightWidth, 0, fade);
        if (width <= 0 || HighlightAlpha == 0)
            return;

        if (reachesTop)
        {
            DrawCornerHighlight(
                context, size, mask, maskBounds,
                fromRight ? WidgetCorner.TopRight : WidgetCorner.TopLeft,
                radius, width, cornersDrawn);
        }

        if (reachesBottom)
        {
            DrawCornerHighlight(
                context, size, mask, maskBounds,
                fromRight ? WidgetCorner.BottomRight : WidgetCorner.BottomLeft,
                radius, width, cornersDrawn);
        }
    }

    /// <summary>Draws a stretch along a horizontal edge.</summary>
    private void DrawHorizontal(
        DrawingContext context,
        Size size,
        WidgetGlowSegment segment,
        bool fromBottom,
        HashSet<WidgetCorner> cornersDrawn)
    {
        var fade = Math.Clamp(VerticalFadeLength, 0, size.Height);
        if (fade <= 0)
            return;

        var shared = segment.Length * size.Width;
        if (shared <= 0)
            return;

        var spread = Math.Max(SpreadAlongEdge, 0);

        var x = (segment.Start * size.Width) - spread;
        var y = fromBottom ? size.Height - fade : 0;
        var area = new Rect(x, y, shared + (spread * 2), fade);

        var across = BuildAcrossFade(
            startX: 0, startY: fromBottom ? 1 : 0,
            endX: 0, endY: fromBottom ? 0 : 1);

        var mask = BuildAlongMask(shared, spread, vertical: false);

        // Mirror of the vertical case: only the across axis is widened.
        var maskBounds = new Rect(x, 0, area.Width, size.Height);

        var radius = CornerRadiusFor(size);
        var reachesLeft = area.Left <= 0;
        var reachesRight = area.Right >= size.Width;

        var alongLeft = reachesLeft ? Math.Min(radius, area.Right) : area.Left;
        var alongRight = reachesRight
            ? Math.Max(size.Width - radius, alongLeft)
            : area.Right;
        var band = new Rect(alongLeft, y, alongRight - alongLeft, fade);

        // As on a vertical edge, the wash keeps its full extent and only the band
        // is cut back to where the corner pass takes over.
        using (context.PushOpacityMask(mask, maskBounds))
        {
            context.FillRectangle(across, area);
        }

        DrawEdgeHighlight(context, mask, maskBounds, band, fade, vertical: false, fromEnd: fromBottom);

        var width = Math.Clamp(EdgeHighlightWidth, 0, fade);
        if (width <= 0 || HighlightAlpha == 0)
            return;

        if (reachesLeft)
        {
            DrawCornerHighlight(
                context, size, mask, maskBounds,
                fromBottom ? WidgetCorner.BottomLeft : WidgetCorner.TopLeft,
                radius, width, cornersDrawn);
        }

        if (reachesRight)
        {
            DrawCornerHighlight(
                context, size, mask, maskBounds,
                fromBottom ? WidgetCorner.BottomRight : WidgetCorner.TopRight,
                radius, width, cornersDrawn);
        }
    }

    /// <summary>
    /// Lays the specular band over the outermost pixels of the glow.
    /// <para>
    /// It is painted on top of the glow that has already been drawn, so it lifts
    /// those pixels towards the highlight colour instead of replacing them, which
    /// is what a reflection on a tinted surface actually does. It shares the
    /// along-edge mask with the glow, so its two ends soften in step with the
    /// light it sits on rather than outliving it as a hard line.
    /// </para>
    /// </summary>
    private void DrawEdgeHighlight(
        DrawingContext context,
        IBrush alongMask,
        Rect maskBounds,
        Rect band,
        double availableDepth,
        bool vertical,
        bool fromEnd)
    {
        // Never wider than the glow it sits on, or it would swallow the falloff.
        var width = Math.Clamp(EdgeHighlightWidth, 0, availableDepth);
        if (width <= 0 || HighlightAlpha == 0 || band.Width <= 0 || band.Height <= 0)
            return;

        var strip = vertical
            ? new Rect(fromEnd ? band.Right - width : band.Left, band.Top, width, band.Height)
            : new Rect(band.Left, fromEnd ? band.Bottom - width : band.Top, band.Width, width);

        using (context.PushOpacityMask(alongMask, maskBounds))
        {
            context.FillRectangle(BuildEdgeFade(vertical, fromEnd), strip);
        }
    }

    /// <summary>
    /// Carries the specular band around a rounded corner.
    /// <para>
    /// A corner cannot be covered by the straight band: the band hugs a line at a
    /// fixed distance from the edge, and once the outline starts to curve that
    /// line falls outside the card and is clipped away. On a 14 DIP radius the
    /// whole band is outside for the first 8 DIP of the turn, so the reflection
    /// simply stops. Here the band is rebuilt as an arc of the card's own corner
    /// circle instead, so it keeps following the outline all the way round.
    /// </para>
    /// </summary>
    private void DrawCornerHighlight(
        DrawingContext context,
        Size size,
        IBrush alongMask,
        Rect maskBounds,
        WidgetCorner corner,
        double radius,
        double width,
        HashSet<WidgetCorner> cornersDrawn)
    {
        if (radius <= 0 || !cornersDrawn.Add(corner))
            return;

        // The straight band's falloff comes from a gradient, and a gradient cannot
        // bend. Concentric sub-bands can, so the corner is built from them, each
        // taking the intensity the gradient would have had partway across it.
        var step = width / CornerBands;

        using (context.PushOpacityMask(alongMask, maskBounds))
        {
            for (var index = 0; index < CornerBands; index++)
            {
                var outer = radius - (index * step);
                var inner = Math.Max(0, outer - step);
                if (outer <= 0 || inner >= outer)
                    break;

                var distance = (index + 0.5) / CornerBands;
                var amount = Math.Pow(1 - distance, Math.Max(FalloffExponent, 0));

                context.DrawGeometry(
                    new SolidColorBrush(HighlightAt(amount)),
                    null,
                    BuildCornerBand(corner, size, radius, outer, inner));
            }
        }
    }

    /// <summary>The ring between two radii of a corner's circle, as a filled shape.</summary>
    private static Geometry BuildCornerBand(
        WidgetCorner corner,
        Size size,
        double radius,
        double outer,
        double inner)
    {
        var (centre, start, end) = CornerArc(corner, size, radius);

        var geometry = new StreamGeometry();

        using var path = geometry.Open();
        AppendArc(path, centre, outer, start, end, begin: true);
        AppendArc(path, centre, inner, end, start, begin: false);
        path.EndFigure(isClosed: true);

        return geometry;
    }

    /// <summary>
    /// Samples an arc of a corner circle into the open path. The arc is walked in
    /// short straight steps rather than with a real arc segment, so the sweep never
    /// depends on how the platform resolves an arc's direction.
    /// </summary>
    private static void AppendArc(
        StreamGeometryContext path,
        Point centre,
        double radius,
        double from,
        double to,
        bool begin)
    {
        for (var step = 0; step <= CornerSteps; step++)
        {
            var angle = (from + ((to - from) * step / CornerSteps)) * Math.PI / 180;
            var point = new Point(
                centre.X + (radius * Math.Cos(angle)),
                centre.Y + (radius * Math.Sin(angle)));

            if (begin && step == 0)
                path.BeginFigure(point, isFilled: true);
            else
                path.LineTo(point);
        }
    }

    /// <summary>
    /// The quarter circle behind a corner, as a centre and the angles its two ends
    /// sit at. Angles are in degrees measured clockwise on screen, so each corner
    /// is the 90 degrees that spans from one of its edges to the other.
    /// </summary>
    private static (Point Centre, double Start, double End) CornerArc(
        WidgetCorner corner,
        Size size,
        double radius) =>
        corner switch
        {
            WidgetCorner.TopLeft => (new Point(radius, radius), 180, 270),
            WidgetCorner.TopRight => (new Point(size.Width - radius, radius), -90, 0),
            WidgetCorner.BottomRight => (new Point(size.Width - radius, size.Height - radius), 0, 90),
            _ => (new Point(radius, size.Height - radius), 90, 180),
        };

    /// <summary>
    /// The band's own fade, running from the outermost pixel inwards. It is short
    /// and steep on purpose: a reflection is brightest exactly at the edge and has
    /// fallen away a couple of DIPs later.
    /// </summary>
    private LinearGradientBrush BuildEdgeFade(bool vertical, bool fromEnd)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = vertical
                ? new RelativePoint(fromEnd ? 1 : 0, 0, RelativeUnit.Relative)
                : new RelativePoint(0, fromEnd ? 1 : 0, RelativeUnit.Relative),
            EndPoint = vertical
                ? new RelativePoint(fromEnd ? 0 : 1, 0, RelativeUnit.Relative)
                : new RelativePoint(0, fromEnd ? 0 : 1, RelativeUnit.Relative),
        };

        for (var step = 0; step <= CurveSteps; step++)
        {
            var distance = step / (double)CurveSteps;
            var fade = Math.Pow(1 - distance, Math.Max(FalloffExponent, 0));

            brush.GradientStops.Add(new GradientStop(HighlightAt(fade), distance));
        }

        return brush;
    }

    /// <summary>
    /// The fade into the card, running from the shared edge inwards. It starts at
    /// full strength on the edge and decays away from it on the shaped curve.
    /// </summary>
    private LinearGradientBrush BuildAcrossFade(double startX, double startY, double endX, double endY)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(startX, startY, RelativeUnit.Relative),
            EndPoint = new RelativePoint(endX, endY, RelativeUnit.Relative),
        };

        for (var step = 0; step <= CurveSteps; step++)
        {
            var distance = step / (double)CurveSteps;
            var fade = Math.Pow(1 - distance, Math.Max(FalloffExponent, 0));

            brush.GradientStops.Add(new GradientStop(
                WithAlpha(fade), distance));
        }

        return brush;
    }

    /// <summary>
    /// The reach along the edge. It holds at full strength across the region the
    /// two widgets share and ramps away over the spread at either end, so the
    /// shared region decides where the light is but not how far it travels. The
    /// ramp uses the same curve as the fade into the card, so the two directions
    /// look like one effect.
    /// </summary>
    private LinearGradientBrush BuildAlongMask(double sharedLength, double spread, bool vertical)
    {
        var mask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(vertical ? 0 : 1, vertical ? 1 : 0, RelativeUnit.Relative),
        };

        var total = sharedLength + (spread * 2);
        var ramp = total > 0 ? Math.Clamp(spread / total, 0, 0.5) : 0;
        var shape = Math.Max(FalloffExponent, 0);

        for (var step = 0; step <= CurveSteps; step++)
        {
            var offset = step / (double)CurveSteps;

            double amount;
            if (ramp <= 0)
            {
                amount = 1;
            }
            else if (offset < ramp)
            {
                amount = Math.Pow(offset / ramp, shape);
            }
            else if (offset > 1 - ramp)
            {
                amount = Math.Pow((1 - offset) / ramp, shape);
            }
            else
            {
                amount = 1;
            }

            var alpha = (byte)Math.Round(255 * Math.Clamp(amount, 0, 1));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), offset));
        }

        return mask;
    }

    /// <summary>The glow colour at a fraction of its full strength.</summary>
    private Color WithAlpha(double amount) =>
        Color.FromArgb(
            (byte)Math.Round(EdgeAlpha * Math.Clamp(amount, 0, 1)),
            GlowColor.R,
            GlowColor.G,
            GlowColor.B);

    /// <summary>The highlight colour at a fraction of its full strength.</summary>
    private Color HighlightAt(double amount) =>
        Color.FromArgb(
            (byte)Math.Round(HighlightAlpha * Math.Clamp(amount, 0, 1)),
            HighlightColor.R,
            HighlightColor.G,
            HighlightColor.B);

    /// <summary>One of the card's four rounded corners.</summary>
    private enum WidgetCorner
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
    }
}
