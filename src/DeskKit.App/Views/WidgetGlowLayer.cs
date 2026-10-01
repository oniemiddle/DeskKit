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

    /// <summary>How many steps are used to approximate the falloff curves.</summary>
    private const int CurveSteps = 16;

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
    /// The card's corner radius. The glow is clipped to the card's outline, so
    /// it stops at the rounded corners rather than squaring off across them.
    /// </summary>
    public double CardCornerRadius { get; set; } = 14;

    /// <summary>Alpha actually used where the light is strongest, after clamping.</summary>
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

        // The glow belongs to the card's surface, so nothing may escape it. The
        // card's own outline is the clip, which is also what keeps the reach
        // along the edge from running off the corners.
        using (context.PushGeometryClip(BuildCardClip(size)))
        {
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
    }

    /// <summary>The card's own outline, which is the whole area the glow may use.</summary>
    private RectangleGeometry BuildCardClip(Size size) =>
        new(new Rect(0, 0, size.Width, size.Height))
        {
            RadiusX = CardCornerRadius,
            RadiusY = CardCornerRadius,
        };

    /// <summary>Draws a stretch along a vertical edge.</summary>
    private void DrawVertical(DrawingContext context, Size size, WidgetGlowSegment segment, bool fromRight)
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

        using (context.PushOpacityMask(BuildAlongMask(shared, spread, vertical: true), area))
        {
            context.FillRectangle(across, area);
        }
    }

    /// <summary>Draws a stretch along a horizontal edge.</summary>
    private void DrawHorizontal(DrawingContext context, Size size, WidgetGlowSegment segment, bool fromBottom)
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

        using (context.PushOpacityMask(BuildAlongMask(shared, spread, vertical: false), area))
        {
            context.FillRectangle(across, area);
        }
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
}
