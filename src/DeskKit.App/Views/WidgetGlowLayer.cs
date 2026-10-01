using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DeskKit.Core.Models;

namespace DeskKit.App.Views;

/// <summary>
/// Draws the magnetism glow as a decoration layer over the card.
/// <para>
/// It is hand-drawn rather than assembled from borders because the shape is not
/// expressible that way: the glow follows the region two widgets actually share,
/// so a single edge can be lit in several separate stretches, several edges can
/// be lit at once, and every stretch needs its own gradient running from the
/// theme colour at the shared edge to transparent further in. A border can only
/// carry one brush, and one brush cannot fade along one axis while also fading
/// along another.
/// </para>
/// <para>
/// Segments arrive as fractions of the card, so the glow stays correct when the
/// widget is resized. It is never hit-testable — it is decoration.
/// </para>
/// </summary>
public sealed class WidgetGlowLayer : Control
{
    /// <summary>How far the glow reaches inwards from the edge, in DIPs.</summary>
    public const double DefaultFadeLength = 14;

    /// <summary>Softening applied to the ends of a stretch, in DIPs.</summary>
    public const double DefaultEndSoftness = 7;

    /// <summary>
    /// Values of the normalised distance from the shared edge at which the
    /// falloff is sampled. A handful of stops is enough to shape the curve, and
    /// the renderer interpolates the rest.
    /// </summary>
    private static readonly double[] FalloffSamples = [0, 0.25, 0.5, 0.75, 1];

    private IReadOnlyList<WidgetGlowSegment> _segments = [];

    /// <summary>The stretches to draw. Setting this redraws the layer.</summary>
    public IReadOnlyList<WidgetGlowSegment> Segments
    {
        get => _segments;
        set
        {
            _segments = value ?? [];
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
    /// Opacity at the shared edge itself. Deliberately well below fully opaque:
    /// the fade is short, so a solid edge colour reads as a painted stripe rather
    /// than as light spilling across from the neighbouring widget.
    /// </summary>
    public double EdgeOpacity { get; set; } = 0.6;

    /// <summary>
    /// Shape of the falloff. Light decays faster near its source, so a linear
    /// ramp looks like a wedge; an exponent above 1 drops quickly at the edge and
    /// then tapers, which reads as a more natural spill.
    /// </summary>
    public double FalloffExponent { get; set; } = 1.5;

    /// <summary>Alpha actually used at the shared edge, after clamping.</summary>
    public byte EdgeAlpha =>
        (byte)Math.Round(255 * Math.Clamp(EdgeOpacity, 0, 1));

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_segments.Count == 0)
            return;

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        foreach (var segment in _segments)
        {
            switch (segment.Edge)
            {
                case WidgetEdge.Left:
                    DrawVertical(context, size, segment, fromRight: false);
                    break;

                case WidgetEdge.Right:
                    DrawVertical(context, size, segment, fromRight: true);
                    break;

                case WidgetEdge.Top:
                    DrawHorizontal(context, size, segment, fromBottom: false);
                    break;

                case WidgetEdge.Bottom:
                    DrawHorizontal(context, size, segment, fromBottom: true);
                    break;
            }
        }
    }

    /// <summary>Draws a stretch along a vertical edge.</summary>
    private void DrawVertical(DrawingContext context, Size size, WidgetGlowSegment segment, bool fromRight)
    {
        var length = segment.Length * size.Height;
        if (length <= 0)
            return;

        var y = segment.Start * size.Height;
        var fade = Math.Min(DefaultFadeLength, size.Width);
        var x = fromRight ? size.Width - fade : 0;

        var area = new Rect(x, y, fade, length);

        // Fades inwards, away from the shared edge.
        var edgeFade = BuildEdgeFade(
            startX: fromRight ? 1 : 0, startY: 0,
            endX: fromRight ? 0 : 1, endY: 0);

        using (context.PushOpacityMask(BuildEndMask(area, length, vertical: true), area))
        {
            context.FillRectangle(edgeFade, area);
        }
    }

    /// <summary>Draws a stretch along a horizontal edge.</summary>
    private void DrawHorizontal(DrawingContext context, Size size, WidgetGlowSegment segment, bool fromBottom)
    {
        var length = segment.Length * size.Width;
        if (length <= 0)
            return;

        var x = segment.Start * size.Width;
        var fade = Math.Min(DefaultFadeLength, size.Height);
        var y = fromBottom ? size.Height - fade : 0;

        var area = new Rect(x, y, length, fade);

        var edgeFade = BuildEdgeFade(
            startX: 0, startY: fromBottom ? 1 : 0,
            endX: 0, endY: fromBottom ? 0 : 1);

        using (context.PushOpacityMask(BuildEndMask(area, length, vertical: false), area))
        {
            context.FillRectangle(edgeFade, area);
        }
    }

    /// <summary>
    /// The gradient that carries the glow from the shared edge inwards. Sampled
    /// along an ease-out curve rather than a straight ramp, so the light falls
    /// away the way light actually does.
    /// </summary>
    private LinearGradientBrush BuildEdgeFade(double startX, double startY, double endX, double endY)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(startX, startY, RelativeUnit.Relative),
            EndPoint = new RelativePoint(endX, endY, RelativeUnit.Relative),
        };

        foreach (var distance in FalloffSamples)
        {
            var fade = Math.Pow(1 - distance, Math.Max(FalloffExponent, 0));
            var alpha = (byte)Math.Round(EdgeAlpha * fade);

            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(alpha, GlowColor.R, GlowColor.G, GlowColor.B), distance));
        }

        return brush;
    }

    /// <summary>
    /// A mask that softens the two ends of a stretch, so a partly lit edge does
    /// not stop with a hard edge. The ramp is capped in absolute terms, because a
    /// proportional ramp would wash out a short stretch completely.
    /// </summary>
    private static LinearGradientBrush BuildEndMask(Rect area, double length, bool vertical)
    {
        var ramp = Math.Min(0.5, DefaultEndSoftness / Math.Max(length, 1));

        var mask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, vertical ? 0 : 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(vertical ? 0 : 1, vertical ? 1 : 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.White, ramp),
                new GradientStop(Colors.White, 1 - ramp),
                new GradientStop(Colors.Transparent, 1),
            },
        };

        return mask;
    }
}
