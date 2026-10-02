using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using DeskKit.App.Localization;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Persistence;
using DeskKit.Platform;
using DeskKit.Platform.Windows;
using DeskKit.Widgets;
using DeskKit.Widgets.Clock;
using DeskKit.Widgets.Localization;
using DeskKit.Runtime;
using DeskKit.Runtime.Views;

namespace DeskKit.App.Diagnostics;

// The shell and what it draws on: the card surface, the glow, snapping and the languages.

internal sealed partial class DesktopLayerSelfTest
{
    /// <summary>
    /// Drives the real shell against a throwaway configuration directory: a
    /// first run must seed a widget, pin its window, write the file, and restore
    /// the same placement on the next start.
    /// </summary>
    private async Task CheckWidgetShellAsync()
    {
        Section("9. widget shell end to end");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var store = new StateStore(Path.Combine(directory, AppPaths.DatabaseFileName), directory);
        var layer = new WindowsDesktopLayerService();

        var shell = CreateShell(directory, store: store, desktopLayer: layer);

        try
        {
            shell.Start();
            await Delay(1500);

            Check("a first run seeds exactly one widget", shell.Widgets.Count == 1,
                $"count={shell.Widgets.Count}");

            Check("the database is written on first run",
                File.Exists(store.DatabasePath), store.DatabasePath);

            Check("the seeded widget is a row of its own",
                WidgetRowCount(store.DatabasePath) == 1,
                $"{WidgetRowCount(store.DatabasePath)} rows");

            if (shell.Runtimes.Count == 0)
            {
                Fail("the seeded widget has a window", "no widget runtime");
                return;
            }

            var runtime = shell.Runtimes[0];
            var hwnd = runtime.Window.Handle;
            var rect = DesktopDiagnostics.TryGetWindowRect(hwnd, out var measured) ? measured : default;

            Note($"seeded placement: x={runtime.Placement.X} y={runtime.Placement.Y} "
                 + $"w={runtime.Placement.Width} h={runtime.Placement.Height}");
            Note($"window position : {runtime.Window.Position} rect={rect}");
            Note($"shell screens   : {string.Join(", ", shell.Screens.Select(s => s.ToString()))}");

            Check("the stored placement matches where the window actually is",
                Math.Abs(runtime.Placement.X - rect.X) <= 1 && Math.Abs(runtime.Placement.Y - rect.Y) <= 1,
                $"placement=({runtime.Placement.X},{runtime.Placement.Y}) rect=({rect.X},{rect.Y})");

            Check("the seeded widget window is visible",
                hwnd != IntPtr.Zero && DesktopDiagnostics.IsVisible(hwnd));

            Check("the seeded widget is pinned below every ordinary window",
                CountOffendersBelow(hwnd) == 0, $"rect={rect}");

            Check("the seeded widget has a usable size",
                rect.Width > 40 && rect.Height > 40, $"rect={rect}");

            // The settings surfaces are only built on demand, so nothing else
            // would notice a XAML resource that fails to resolve at runtime.
            Check("the seeded widget can build its settings view",
                runtime.ViewModel.CreateSettingsView() is not null);

            Check("the settings window XAML loads", CanLoadSettingsWindow());

            // The one service this section otherwise stands in for. It asks the shell
            // for an icon and copies it out of GDI, which is the path with no other
            // coverage: the registry elsewhere is built with a null icon loader.
            var icon = await new WindowsShellIconLoader().LoadAsync(
                Path.Combine(Environment.SystemDirectory, "notepad.exe"));

            Check("the shell icon loader returns an icon",
                icon is not null && icon.PixelSize.Width >= 16 && icon.PixelSize.Height >= 16,
                icon is null ? "no icon" : $"{icon.PixelSize.Width}x{icon.PixelSize.Height}");

            var seeded = runtime.Placement;

            // Restart against the same directory.
            shell.Dispose();
            await Delay(300);

            var reloaded = new StateStore(Path.Combine(directory, AppPaths.DatabaseFileName), directory).Load();

            Check("the placement survives a restart",
                reloaded.Widgets.Count == 1
                && reloaded.Widgets[0].InstanceId == seeded.InstanceId
                && reloaded.Widgets[0].WidgetId == seeded.WidgetId
                && reloaded.Widgets[0].X == seeded.X
                && reloaded.Widgets[0].Y == seeded.Y,
                reloaded.Widgets.Count == 1
                    ? $"saved {seeded.X},{seeded.Y} reloaded {reloaded.Widgets[0].X},{reloaded.Widgets[0].Y}"
                    : $"reloaded count={reloaded.Widgets.Count}");
        }
        catch (Exception ex)
        {
            Fail("the widget shell ran end to end", ex.Message);
        }
        finally
        {
            shell.Dispose();

            RemoveTemporary(directory);
        }
    }

    /// <summary>
    /// Every widget must reserve a usable drag region at the top, and the
    /// affordance for it must be hidden at rest, revealed on hover, and painted
    /// in the accent colour while the widget is being moved.
    /// <para>
    /// This runs against every built-in widget rather than one of them, because
    /// the case that motivated the region is the sticky note: its text boxes fill
    /// the whole surface, so without a reserved region it cannot be moved at all.
    /// </para>
    /// </summary>
    private async Task CheckDragHandleChromeAsync()
    {
        Section("10. drag handle chrome and magnetism glow");
        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var registry = BuiltInRegistry();

        var shell = CreateShell(directory, registry: registry);

        try
        {
            shell.Start();

            foreach (var provider in registry.Providers)
                shell.AddWidget(provider);

            await Delay(2000);

            // One window per widget type is enough; a first run already seeded a
            // clock, so duplicates are dropped here.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sampled = 0;

            foreach (var runtime in shell.Runtimes)
            {
                if (!seen.Add(runtime.Placement.WidgetId))
                    continue;

                sampled++;

                var name = WidgetText.Value(
                    registry.Find(runtime.Placement.WidgetId)?.Descriptor.DisplayName
                    ?? runtime.Placement.WidgetId);
                var window = runtime.Window;
                var handle = window.DragHandleBounds;
                var card = window.CardBounds;

                Note($"widget          : {name}");
                Note($"  window        : {window.Bounds.Width}x{window.Bounds.Height}");
                Note($"  card          : {card}");
                Note($"  drag strip    : {handle}");
                Note($"  content       : {window.ContentBounds}");

                Check($"[{name}] has a drag strip across the top of the card",
                    handle.Y - card.Y <= 1 && handle.Height >= 12 && handle.Width > 60,
                    $"handle={handle} card={card}");

                Check($"[{name}] the drag strip spans the card width",
                    handle.Width >= card.Width - 2,
                    $"handle.Width={handle.Width} card.Width={card.Width}");

                // The strip is an overlay, so the content still occupies the whole
                // card rather than being pushed down by it.
                Check($"[{name}] the drag strip does not reserve layout space",
                    window.ContentBounds.Top <= handle.Top + 0.5,
                    $"content.Top={window.ContentBounds.Top} strip.Top={handle.Top}");

                Check($"[{name}] the drag strip background is transparent",
                    IsFullyTransparent(window.DragHandleBackground),
                    DescribeBrush(window.DragHandleBackground));

                Check($"[{name}] hovering the drag strip does not change the cursor",
                    window.DragHandleCursor is null,
                    window.DragHandleCursor?.ToString() ?? "no cursor set");

                Check($"[{name}] the bar casts a shadow so it stays visible on light backgrounds",
                    window.DragBarShadowCount > 0, $"shadows={window.DragBarShadowCount}");

                // The card is inset so its own drop shadow has room to render.
                Check($"[{name}] the card is inset so its drop shadow has room",
                    card.Width < window.Bounds.Width && card.Height < window.Bounds.Height,
                    $"card={card.Width}x{card.Height} window={window.Bounds.Width}x{window.Bounds.Height}");

                Check($"[{name}] the magnetism glow is absent until something snaps",
                    !window.IsSnapGlowVisible && window.GlowSegments.Count == 0);

                Check($"[{name}] the magnetism glow cannot swallow pointer input",
                    !window.SnapGlowHitTestable);

                // The fade has to be a soft edge, not a wash across the widget.
                // A vertical stretch fades across the card's width, which is its
                // long side, so it is given more room than a horizontal one.
                Check($"[{name}] the glow fades out over a short distance",
                    window.GlowVerticalFadeLength is > 0 and <= 20
                    && window.GlowHorizontalFadeLength is > 0 and <= 40,
                    $"vertical={window.GlowVerticalFadeLength} horizontal={window.GlowHorizontalFadeLength} DIP");

                Check($"[{name}] a vertical stretch gets a wider horizontal fade than a horizontal one",
                    window.GlowHorizontalFadeLength > window.GlowVerticalFadeLength,
                    $"horizontal={window.GlowHorizontalFadeLength} vertical={window.GlowVerticalFadeLength}");

                // The glow is a surface effect: it lights the card, it does not
                // spill around it the way an outer glow would.
                // The check is made further down, once the layer is visible and
                // therefore has been laid out.

                // The shared region is where the light is, not how far it reaches.
                Check($"[{name}] the light reaches along the edge beyond the shared region",
                    window.GlowSpreadAlongEdge is >= 8 and <= 60,
                    $"spread={window.GlowSpreadAlongEdge} DIP");

                // With a fade this short, a solid edge colour reads as a painted
                // stripe rather than as light spilling in.
                Check($"[{name}] the glow is translucent at the edge, not opaque",
                    window.GlowEdgeAlpha is > 80 and < 230,
                    $"edge alpha={window.GlowEdgeAlpha}/255");

                Check($"[{name}] the glow falls off on a curve, not a straight ramp",
                    window.GlowFalloffExponent > 1,
                    $"exponent={window.GlowFalloffExponent}");

                // A lit edge shows a specular reflection: the outermost pixels go
                // brighter than the light that reached them, and the falloff
                // happens just behind. Without it the brightest thing on the card
                // is still a tint of the accent colour.
                Check($"[{name}] the outermost edge carries a brighter specular band",
                    window.GlowHighlightAlpha > window.GlowEdgeAlpha,
                    $"highlight={window.GlowHighlightAlpha}/255 glow={window.GlowEdgeAlpha}/255");

                Check($"[{name}] the specular band is a line, not a second glow",
                    window.GlowHighlightWidth is > 0 and <= 3
                    && window.GlowHighlightWidth < window.GlowVerticalFadeLength
                    && window.GlowHighlightWidth < window.GlowHorizontalFadeLength,
                    $"band={window.GlowHighlightWidth} DIP, fades={window.GlowVerticalFadeLength}/{window.GlowHorizontalFadeLength}");

                Check($"[{name}] the specular band is a light tint, not a hard white stroke",
                    window.GlowHighlightColor.R >= 0xE0
                    && window.GlowHighlightColor.G >= 0xE0
                    && window.GlowHighlightColor.B >= 0xE0
                    && window.GlowHighlightColor != Colors.White,
                    window.GlowHighlightColor.ToString());

                // Several stretches, on several edges, at once.
                window.SetSnapHighlight(
                [
                    new WidgetGlowSegment(WidgetEdge.Right, 0.25, 0.5),
                    new WidgetGlowSegment(WidgetEdge.Bottom, 0, 0.3),
                ]);

                Check($"[{name}] several stretches on several edges light up together",
                    window.GlowSegments.Count == 2
                    && window.GlowSegments[0].Edge == WidgetEdge.Right
                    && window.GlowSegments[1].Edge == WidgetEdge.Bottom,
                    Describe(window.GlowSegments));

                Check($"[{name}] a lit stretch keeps its position along the edge",
                    Math.Abs(window.GlowSegments[0].Start - 0.25) < 0.001
                    && Math.Abs(window.GlowSegments[0].Length - 0.5) < 0.001,
                    Describe(window.GlowSegments));

                // Now that the glow is visible it has been laid out, so its own
                // rectangle can be compared with the card's. The layer is inside
                // the card precisely so the light cannot escape it. The layer is
                // collapsed while hidden, so it needs a frame to be arranged.
                await Delay(150);

                var glow = window.GlowBounds;
                var glowReport = $"card={card} glow={glow}";

                Check($"[{name}] the magnetism glow paints only on the card's surface",
                    IsContainedIn(card, glow),
                    glowReport);

                Check($"[{name}] the glow layer shares the card's rectangle",
                    Math.Abs(glow.Left - card.Left) < 0.5
                    && Math.Abs(glow.Top - card.Top) < 0.5
                    && Math.Abs(glow.Width - card.Width) < 0.5
                    && Math.Abs(glow.Height - card.Height) < 0.5,
                    glowReport);

                window.SetSnapHighlight([]);
                Check($"[{name}] the magnetism glow can be turned off",
                    !window.IsSnapGlowVisible && window.GlowSegments.Count == 0);

                window.SetDragAffordance(hovered: false, dragging: false);
                Check($"[{name}] the bar is absent at rest",
                    window.DragBarOpacity == 0, $"opacity={window.DragBarOpacity}");

                window.SetDragAffordance(hovered: true, dragging: false);
                Check($"[{name}] the bar appears on hover",
                    window.DragBarOpacity is > 0.5 and < 1
                    && ReferenceEquals(window.DragBarBackground, WidgetWindow.DragBarIdleBrush),
                    $"opacity={window.DragBarOpacity}");

                window.SetDragAffordance(hovered: true, dragging: true);
                Check($"[{name}] the bar turns accent-coloured while dragging",
                    window.DragBarOpacity == 1
                    && ReferenceEquals(window.DragBarBackground, WidgetWindow.DragBarActiveBrush),
                    $"opacity={window.DragBarOpacity}");

                window.SetDragAffordance(hovered: false, dragging: false);
            }

            Check("every built-in widget was sampled", sampled >= registry.Providers.Count,
                $"sampled={sampled} of {registry.Providers.Count}");

            await CheckMagnetismAsync(registry, shell);
        }
        catch (Exception ex)
        {
            Fail("the drag handle chrome was verified", ex.Message);
        }
        finally
        {
            shell.Dispose();

            RemoveTemporary(directory);
        }
    }

    /// <summary>
    /// Drives the real snap strategy the drag handler calls: a widget moved next
    /// to another must land with the configured gap rather than flush against it,
    /// and both must light up. Moving away again must clear the highlight.
    /// </summary>
    private async Task CheckMagnetismAsync(WidgetRegistry registry, WidgetShell shell)
    {
        Section("11. magnetic snapping");

        var runtimes = shell.Runtimes;
        if (runtimes.Count < 2)
        {
            Fail("at least two widgets exist to snap together", $"count={runtimes.Count}");
            return;
        }

        var moving = runtimes[^1];
        var anchor = runtimes[0];

        if (!DesktopDiagnostics.TryGetWindowRect(anchor.Window.Handle, out var anchorRect))
        {
            Fail("the anchor widget's rect is readable", "GetWindowRect failed");
            return;
        }

        Note($"anchor          : {anchorRect}");
        Note($"moving          : {moving.Window.Position}");

        // Snapping is measured between visible cards, so the assertions here are
        // too: the transparent glow margin must not show up as part of the gap.
        var margin = (int)Math.Round(WidgetWindow.GlowMargin);
        var anchorCard = Inset(anchorRect, margin);
        var movingCardSize = moving.Window.CardBounds.Size;
        var movingCardSizePx = new PixelSize(
            (int)Math.Round(movingCardSize.Width), (int)Math.Round(movingCardSize.Height));

        Note($"anchor card     : {anchorCard}");
        Note($"moving card     : {movingCardSizePx}");

        // Aim for just to the right of the anchor's card, deliberately a few
        // pixels short of the gap so nothing but magnetism can produce the value.
        var expectedWindowX = anchorCard.Right + WidgetSnapEngine.DefaultGap - margin;
        var proposed = new PixelPoint(expectedWindowX - 5, anchorCard.Y + 20);

        var snapped = moving.Window.SnapStrategy?.Invoke(proposed) ?? proposed;
        var snappedCard = new PixelRect(
            new PixelPoint(snapped.X + margin, snapped.Y + margin), movingCardSizePx);

        Note($"proposed        : {proposed}  (card would be at {proposed.X + margin})");
        Note($"snapped to      : {snapped}  (card at {snappedCard.X})");

        Check("the snap strategy is installed on every widget",
            moving.Window.SnapStrategy is not null);

        Check("a widget dragged next to another lands exactly one gap away",
            snappedCard.X - anchorCard.Right == WidgetSnapEngine.DefaultGap,
            $"visible gap={snappedCard.X - anchorCard.Right}, expected {WidgetSnapEngine.DefaultGap}");

        Check("the snapped widgets are not touching",
            snappedCard.X - anchorCard.Right >= WidgetSnapEngine.DefaultGap,
            $"gap={snappedCard.X - anchorCard.Right}");

        // The overlap between the two cards decides which stretch lights up.
        var movingGlow = moving.Window.GlowSegments;
        var anchorGlow = anchor.Window.GlowSegments;

        Note($"moving glow     : {Describe(movingGlow)}");
        Note($"anchor glow     : {Describe(anchorGlow)}");

        Check("the dragged widget lights up on the edge facing its neighbour",
            movingGlow.Count == 1 && movingGlow[0].Edge == WidgetEdge.Left,
            Describe(movingGlow));

        Check("the widget it snapped to lights up on the matching edge",
            anchorGlow.Count == 1 && anchorGlow[0].Edge == WidgetEdge.Right,
            Describe(anchorGlow));

        // Both cards are level at the top but differ in height, so only the
        // region they actually share is lit. This is the whole point of
        // measuring the overlap rather than lighting the entire edge.
        var sharedHeight = Math.Min(snappedCard.Bottom, anchorCard.Bottom)
                           - Math.Max(snappedCard.Y, anchorCard.Y);
        var expectedLength = sharedHeight / (double)snappedCard.Height;

        Note($"shared height   : {sharedHeight} of {snappedCard.Height}");

        Check("the lit stretch is exactly the region the two widgets share",
            Math.Abs(movingGlow[0].Length - expectedLength) < 0.02,
            $"lit {movingGlow[0].Length:P0}, shared {expectedLength:P0}");

        Check("a neighbour shorter than the widget leaves the rest of the edge unlit",
            movingGlow[0].Length < 0.99 && movingGlow[0].Start < 0.01,
            Describe(movingGlow));

        // Now the case the whole feature exists for: a neighbour that only
        // covers part of the edge must light only that part.
        var partial = WidgetSnapEngine.GlowSegments(
            new PixelRect(0, 0, 200, 100),
            [new PixelRect(208, 40, 200, 30)]);

        Note($"partial overlap : {Describe(partial)}");

        Check("a neighbour covering part of an edge lights only that part",
            partial.Count == 1
            && partial[0].Edge == WidgetEdge.Right
            && Math.Abs(partial[0].Start - 0.4) < 0.01
            && Math.Abs(partial[0].Length - 0.3) < 0.01,
            Describe(partial));

        // Two neighbours down the same side light two separate stretches.
        var twoOnOneEdge = WidgetSnapEngine.GlowSegments(
            new PixelRect(0, 0, 200, 300),
            [new PixelRect(208, 0, 200, 50), new PixelRect(208, 200, 200, 60)]);

        Note($"two on one edge : {Describe(twoOnOneEdge)}");

        Check("two neighbours on the same side light two separate stretches",
            twoOnOneEdge.Count == 2
            && twoOnOneEdge.All(s => s.Edge == WidgetEdge.Right)
            && twoOnOneEdge[0].End <= twoOnOneEdge[1].Start + 0.01,
            Describe(twoOnOneEdge));

        // A diagonal placement touches on two sides at once.
        var diagonal = WidgetSnapEngine.GlowSegments(
            new PixelRect(0, 0, 100, 100),
            [new PixelRect(108, 108, 100, 100)]);

        Note($"diagonal        : {Describe(diagonal)}");

        Check("a diagonal placement lights two edges at once",
            diagonal.Select(s => s.Edge).Distinct().Count() == 2,
            Describe(diagonal));

        // Now move far away: nothing should be highlighted any more.
        var away = new PixelPoint(proposed.X + 600, proposed.Y + 400);
        var unsnapped = moving.Window.SnapStrategy?.Invoke(away) ?? away;

        Check("moving away from everything snaps to nothing",
            unsnapped == away, $"got {unsnapped}");

        Check("the highlight clears once nothing is snapped",
            !moving.Window.IsSnapGlowVisible && moving.Window.GlowSegments.Count == 0
            && !anchor.Window.IsSnapGlowVisible && anchor.Window.GlowSegments.Count == 0);

        await Delay(50);
    }

    /// <summary>
    /// Rasterises the glow layer offscreen and reads its pixels back.
    /// <para>
    /// Everything else about the glow is checked through its exposed geometry,
    /// which cannot tell whether the drawing actually lands on the card. The
    /// corner band in particular is a shape built from sampled arcs, and a
    /// plausible-looking mistake — the wrong sweep, an arc pointing the wrong way —
    /// still builds and still passes every property check. Rendering it and
    /// sampling the corner is the only way to see that the reflection really does
    /// follow the curve.
    /// </para>
    /// </summary>
    private void CheckGlowPixels()
    {
        Section("12. glow pixels");

        const int width = 260;
        const int height = 130;
        const int radius = 14;

        // A full-height stretch on the right edge, so the light reaches both
        // corners and they are the only thing being tested.
        var lit = RenderGlow(width, height, radius, [new WidgetGlowSegment(WidgetEdge.Right, 0, 1)], highlight: true);
        var glowOnly = RenderGlow(width, height, radius, [new WidgetGlowSegment(WidgetEdge.Right, 0, 1)], highlight: false);

        // Just inside the top-right corner's arc, halfway between the edge and the
        // inner side of the band. With only the straight band this pixel is left
        // dark, because the band runs outside the card once the outline curves.
        var corner = (X: 255, Y: 4);
        var cornerCurve = Alpha(lit, width, corner.X, corner.Y);
        var cornerCurveWithout = Alpha(glowOnly, width, corner.X, corner.Y);

        Check("the reflection follows the card's rounded corner",
            cornerCurve > cornerCurveWithout + 20,
            $"at ({corner.X},{corner.Y}) alpha {cornerCurveWithout} without the band, {cornerCurve} with it");

        // The same band, on the straight part of the edge.
        var straight = (X: 259, Y: 65);
        Check("the reflection is still on the straight edge",
            Alpha(lit, width, straight.X, straight.Y) > Alpha(glowOnly, width, straight.X, straight.Y) + 20,
            $"at ({straight.X},{straight.Y}) alpha {Alpha(glowOnly, width, straight.X, straight.Y)} -> {Alpha(lit, width, straight.X, straight.Y)}");

        // Two pixels that sit inside the layer's rectangle but outside the card's
        // rounded outline. Nothing may be painted there, or the band would be a
        // rectangle again rather than something that follows the curve.
        var outsideCorner = (X: 259, Y: 1);
        Check("nothing is painted outside the card's rounded corner",
            Alpha(lit, width, outsideCorner.X, outsideCorner.Y) == 0,
            $"at ({outsideCorner.X},{outsideCorner.Y}) alpha {Alpha(lit, width, outsideCorner.X, outsideCorner.Y)}");

        var outsideFlat = (X: 0, Y: 65);
        Check("nothing is painted on the edge facing away from the light",
            Alpha(lit, width, outsideFlat.X, outsideFlat.Y) == 0,
            $"at ({outsideFlat.X},{outsideFlat.Y}) alpha {Alpha(lit, width, outsideFlat.X, outsideFlat.Y)}");

        // The corner band is drawn once even though a diagonal placement reaches
        // it from two edges at once. Both points are on the same edge of the same
        // render, so they differ only in whether a corner is involved; if the
        // corner were drawn a second time it would come out noticeably brighter
        // than the straight part beside it.
        var diagonal = RenderGlow(
            width, height, radius,
            [
                new WidgetGlowSegment(WidgetEdge.Right, 0.5, 0.5),
                new WidgetGlowSegment(WidgetEdge.Bottom, 0.5, 0.5),
            ],
            highlight: true);

        var cornerShared = (X: 255, Y: 125);
        var straightBeside = (X: 259, Y: 100);
        var shared = Alpha(diagonal, width, cornerShared.X, cornerShared.Y);
        var single = Alpha(diagonal, width, straightBeside.X, straightBeside.Y);

        Check("a corner reached from two edges is lit once, not twice",
            shared > 0 && shared <= single + 12,
            $"corner ({cornerShared.X},{cornerShared.Y})={shared} vs straight ({straightBeside.X},{straightBeside.Y})={single}");
    }

    /// <summary>
    /// Verifies that a widget window can be given a platform surface material, and
    /// that the layout agrees with it.
    /// <para>
    /// A material fills the window's whole rectangle, so the card has to fill the
    /// window too — otherwise the widget would be a card sitting on a visible plate
    /// of material. The card also has to let the material through, and the rounded
    /// corners and the shadow have to move to the platform, since the card is no
    /// longer inset from anything.
    /// </para>
    /// </summary>
    private async Task CheckWindowMaterialAsync()
    {
        Section("13. window material");

        var materials = new WindowsWindowMaterialService();
        var resolved = materials.Resolve(materials.Default);

        Note($"requested       : {materials.Default}");
        Note($"resolved        : {resolved}");
        Note($"os build        : {Environment.OSVersion.Version.Build}");
        Note($"supported       : {materials.IsSupported}");

        Check("a material is reported as supported on a build that has one",
            materials.IsSupported == (resolved != WidgetMaterial.None),
            $"resolved={resolved} supported={materials.IsSupported}");

        // The no-material path is exercised on every machine, not just ones that
        // lack a material: naming a transparency level while applying "no material"
        // would take away the per-pixel alpha the classic window depends on and put
        // a visible rectangle around the card.
        var classic = new WidgetWindow(new WindowsDesktopLayerService(), materials, WidgetMaterial.None);
        var classicHint = string.Join('|', classic.TransparencyLevelHint);

        Check("applying no material leaves the transparent window alone",
            classic.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent),
            $"hint={classicHint}");

        Check("no material keeps the card inset and shadowed the classic way",
            classic.CardMargin != default && classic.CardShadow.Count > 0,
            $"margin={classic.CardMargin} shadows={classic.CardShadow.Count}");

        if (resolved == WidgetMaterial.None)
        {
            Check("an unsupported material falls back rather than pretending", true,
                "no material on this build, so the classic window is correct");
            return;
        }

        var window = new WidgetWindow(new WindowsDesktopLayerService(), materials, resolved)
        {
            CardBackground = ThemeService.CardBrushFor(resolved),
            WidgetContent = new TextBlock { Text = "material" },
            Width = 240,
            Height = 120,
            ShowInTaskbar = false,
            Position = new PixelPoint(80, 560),
        };

        window.Show();
        await Delay(1200);

        var handle = window.Handle;

        // The window's own rectangle, inset by the margin, is what the card should
        // cover. With a material that margin is nothing.
        var card = window.CardBounds;
        Note($"window          : {window.Bounds.Width}x{window.Bounds.Height}");
        Note($"card            : {card}");

        Check("the card fills the window when the window is the surface",
            window.CardMargin == default
            && Math.Abs(card.Width - window.Bounds.Width) < 0.5
            && Math.Abs(card.Height - window.Bounds.Height) < 0.5,
            $"margin={window.CardMargin} card={card.Width}x{card.Height} window={window.Bounds.Width}x{window.Bounds.Height}");

        Check("the card hands its rounded corners to the platform",
            window.CardCornerRadius == default,
            window.CardCornerRadius.ToString());

        Check("the card hands its drop shadow to the platform",
            window.CardShadow.Count == 0,
            $"shadows={window.CardShadow.Count}");

        // The card must not dilute the material at all: whatever is painted over it
        // is subtracted from the surface the material is there to provide, and a
        // card that is merely "translucent" still washes it out.
        var cardAlpha = window.CardBackground switch
        {
            ISolidColorBrush solid => solid.Color.A,
            _ => (byte)255,
        };

        Check("the default background is the material and nothing else",
            cardAlpha == 0,
            $"card alpha={cardAlpha}/255, so {(255 - cardAlpha) / 255.0:P0} of the material survives");

        // The compositor has to have been asked for a backdrop. DWMSBT_AUTO counts:
        // that is what a window whose backdrop came from the transparency hint
        // reports, and it is what makes the hint path work.
        var backdrop = DesktopDiagnostics.GetSystemBackdropType(handle);
        Note($"backdrop        : {backdrop}");
        Note($"no redirection  : {DesktopDiagnostics.UsesNoRedirectionBitmap(handle)}");

        Check("the compositor was asked to draw a backdrop behind the widget",
            backdrop != DesktopDiagnostics.BackdropNone,
            $"backdrop={backdrop}");

        Check("the material is live on the real window",
            materials.IsActive(window),
            "read back from the DWM");

        // The material must not cost the window its pinning: the card being the
        // window changes the surface, not the z-order rules.
        Check("a material window is still not on the taskbar",
            !DesktopDiagnostics.HasAppWindowStyle(handle),
            $"exstyle=0x{DesktopDiagnostics.GetExtendedStyle(handle):X}");

        window.Close();
        await Delay(200);

        CheckWidgetForegroundThemes();
        CheckLocalization();
        await CheckPlacementAuthorshipAsync();
        await CheckStorageLayoutAsync();
        await CheckWidgetSettingsMigrationAsync();
        await CheckDegradedStorageNoticeAsync();
        await CheckNoticeWindowAsync();
        await CheckWriteSurvivesBeingKilledAsync();
        await CheckTickOrderAsync();
    }

    /// <summary>
    /// A widget must not be ticked before it has started.
    /// </summary>
    /// <remarks>
    /// The order widgets are written against is shown, then started, then ticked: a tick
    /// that arrived while a view model was still starting would reach a view that does
    /// not exist yet. This is the only place that order can be observed. The tick comes
    /// from a dispatcher timer on the UI thread and the start runs synchronously on that
    /// same thread, so in a unit test no tick can interleave the start, and a test
    /// written there would pass whichever order the code used. Here the widget itself
    /// reports, from inside its own start, how many widgets are already on the shared
    /// timer.
    /// </remarks>
    private async Task CheckTickOrderAsync()
    {
        Section("21. tick ordering");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-tick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var ticks = new TickService(TimeSpan.FromMilliseconds(50));
        var probe = new TickProbeProvider(ticks);

        // The real registry plus the probe, so the probe shares the timer with the
        // seeded clock: the question is whether the timer holds a widget that has not
        // finished starting, not whether a timer exists.
        var registry = BuiltInRegistry();
        registry.Register(probe);

        var shell = CreateShell(directory, registry: registry, ticks: ticks);

        try
        {
            shell.Start();
            await Delay(400);

            // Whatever the seeded widgets left behind. Read rather than assumed to be
            // zero, because the timer is shared with every other widget.
            var subscribedBefore = ticks.SubscriberCount;
            Note($"subscribed before : {subscribedBefore}");

            Check("a widget can be added to a running shell", shell.AddWidget(probe) is not null);

            if (probe.Created is not { } viewModel)
            {
                Fail("the probe widget started", "it was never created");
                return;
            }

            Check("a widget is not on the shared timer while it is still starting",
                viewModel.SubscribersWhileStarting == subscribedBefore,
                $"before={subscribedBefore}, during start={viewModel.SubscribersWhileStarting}");

            Check("a widget is on the shared timer once it has started",
                ticks.SubscriberCount == subscribedBefore + 1,
                $"subscribers={ticks.SubscriberCount}");

            await Delay(400);

            Check("the tick reaches a running widget",
                viewModel.Ticks > 0,
                $"ticks={viewModel.Ticks}");

            var ticksBeforeRemoval = viewModel.Ticks;
            shell.RemoveWidget(viewModel);
            await Delay(300);

            Check("a widget that was removed is off the shared timer",
                ticks.SubscriberCount == subscribedBefore,
                $"subscribers={ticks.SubscriberCount}");

            Check("a widget that was removed is not ticked again",
                viewModel.Ticks == ticksBeforeRemoval,
                $"{ticksBeforeRemoval} -> {viewModel.Ticks}");
        }
        catch (Exception ex)
        {
            Fail("the tick ordering ran", ex.Message);
        }
        finally
        {
            shell.Dispose();
            RemoveTemporary(directory);
        }
    }

    /// <summary>Registers the tick probe, and hands its widget back for inspection.</summary>
    private sealed class TickProbeProvider(TickService ticks) : IWidgetProvider
    {
        public const string WidgetId = "selftest.tick-probe";

        public WidgetDescriptor Descriptor { get; } = new(
            WidgetId, "Tick_Probe", null, 220, 120, 120, 80, PreventActivation: true);

        public TickProbeViewModel? Created { get; private set; }

        public WidgetViewModel Create(WidgetContext context) =>
            Created = new TickProbeViewModel(context, ticks);
    }

    /// <summary>
    /// A widget whose only job is to report what it sees while it starts, and to count
    /// the ticks it is given once it is running.
    /// </summary>
    private sealed class TickProbeViewModel(WidgetContext context, TickService ticks)
        : WidgetViewModel(context), ITickAware
    {
        /// <summary>How many widgets were on the shared timer as this one started.</summary>
        public int SubscribersWhileStarting { get; private set; } = -1;

        public int Ticks { get; private set; }

        public override Control CreateView() => new TextBlock { Text = "tick probe" };

        public override void Start() => SubscribersWhileStarting = ticks.SubscriberCount;

        public void OnTick(DateTimeOffset now) => Ticks++;
    }

    /// <summary>
    /// Verifies that the app really has two languages and that switching between
    /// them reaches every string.
    /// <para>
    /// Resource keys are looked up by name at runtime, so a descriptor naming a key
    /// that no resource file defines does not fail to build and does not throw — it
    /// renders as the key itself, or as nothing. Both are quiet, which is why they
    /// are checked here rather than assumed.
    /// </para>
    /// </summary>
    private void CheckLocalization()
    {
        Section("14. languages");

        var registry = new WidgetRegistry();
        foreach (var provider in BuiltInWidgets.CreateProviders(new NullShellIconLoader()))
            registry.Register(provider);

        var language = new LanguageService();
        Note($"system culture  : {language.SystemCulture.Name}");
        Note($"offered         : {string.Join(", ", LanguageSetting.Offered)}");

        // Every widget has to be named and described in every language, or the
        // settings window shows a raw key to the user.
        var missing = new List<string>();

        foreach (var provider in registry.Providers)
        {
            var key = provider.Descriptor.DisplayName;
            if (WidgetText.Observable(key) is null)
                missing.Add(key);
        }

        Check("every widget names a resource key this build owns",
            missing.Count == 0,
            missing.Count == 0 ? $"{registry.Providers.Count} widgets" : string.Join(", ", missing));

        // Both languages must actually produce text, and it must differ, or the
        // second resource file is not being read at all.
        language.Apply("en");
        var english = CollectStrings(registry);

        language.Apply("zh-Hans");
        var chinese = CollectStrings(registry);

        Note($"english sample  : {english.WidgetName}");
        Note($"chinese sample  : {chinese.WidgetName}");

        Check("the English strings are not empty",
            english.WidgetName.Length > 0 && english.WindowTitle.Length > 0,
            $"widget={english.WidgetName} window={english.WindowTitle}");

        Check("switching language changes what the strings say",
            english.WidgetName != chinese.WidgetName
            && english.WindowTitle != chinese.WindowTitle,
            $"widget {english.WidgetName} -> {chinese.WidgetName}, " +
            $"window {english.WindowTitle} -> {chinese.WindowTitle}");

        Check("the Chinese strings are the Chinese resource file's",
            chinese.WidgetName == "时钟" && chinese.WindowTitle == "DeskKit 设置",
            $"widget={chinese.WidgetName} window={chinese.WindowTitle}");

        Check("the English strings are the invariant resource file's",
            english.WidgetName == "Clock" && english.WindowTitle == "DeskKit settings",
            $"widget={english.WidgetName} window={english.WindowTitle}");

        // A language the build has never heard of must still leave a working UI.
        language.Apply("xx-NotReal");
        Check("an unknown language falls back rather than blanking the UI",
            WidgetText.Value(WidgetText.ClockName).Length > 0
            && AppLanguage.Instance.Tray_Exit.CurrentText().Length > 0,
            $"widget={WidgetText.Value(WidgetText.ClockName)} " +
            $"tray={AppLanguage.Instance.Tray_Exit.CurrentText()}");

        // Following the system has to reach real resources rather than falling
        // through to the key, which is what would happen if the hierarchy walk
        // found nothing. Which language it lands on is the machine's business, so
        // that part is reported rather than asserted.
        language.Apply(LanguageSetting.System);
        var systemName = WidgetText.Value(WidgetText.ClockName);

        Check("following the system resolves to a real language",
            systemName.Length > 0 && systemName != WidgetText.ClockName,
            $"system={language.SystemCulture.Name} -> {systemName}");
    }

    /// <summary>Every built-in widget, registered, as the shell would have it.</summary>
    private static WidgetRegistry BuiltInRegistry()
    {
        var registry = new WidgetRegistry();

        foreach (var provider in BuiltInWidgets.CreateProviders(new NullShellIconLoader()))
            registry.Register(provider);

        return registry;
    }

    /// <summary>How many widgets the database holds.</summary>
    private static long WidgetRowCount(string databasePath) =>
        Convert.ToInt64(Query(databasePath, "SELECT COUNT(*) FROM Widgets;"), CultureInfo.InvariantCulture);

    private static LanguageSample CollectStrings(WidgetRegistry registry) =>
        new(
            WidgetText.Value(
                registry.Find(ClockWidgetProvider.WidgetId)?.Descriptor.DisplayName
                ?? ClockWidgetProvider.WidgetId),
            AppLanguage.Instance.App_Title.CurrentText());

    /// <summary>
    /// Verifies that widget content is legible against a bare material.
    /// <para>
    /// Making the material the whole background moved the widget content from a
    /// dark card onto Mica, which is light in a light theme. Content that was
    /// light-on-dark would then be light-on-light. These resources are looked up by
    /// key at runtime, so a missing one does not fail loudly — it renders as
    /// nothing — which is why the lookup itself is checked rather than assumed.
    /// </para>
    /// </summary>
    private void CheckWidgetForegroundThemes()
    {
        var application = Application.Current;
        if (application is null)
        {
            Check("the widget foreground resources resolve", false, "no application");
            return;
        }

        var dark = ResolveForeground(application, ThemeVariant.Dark);
        var light = ResolveForeground(application, ThemeVariant.Light);

        Check("the widget foreground resources resolve in both themes",
            dark is not null && light is not null,
            $"dark={dark} light={light}");

        if (dark is null || light is null)
            return;

        Note($"widget foreground: dark {dark} light {light}");

        // A material is light in a light theme and dark in a dark one, so the
        // content has to be the opposite of its own theme variant. Getting this
        // backwards is exactly the invisible-text bug this guards against.
        Check("widget content is light in the dark theme",
            Luminance(dark.Value) > 0.5,
            $"luminance={Luminance(dark.Value):F2}");

        Check("widget content is dark in the light theme",
            Luminance(light.Value) < 0.5,
            $"luminance={Luminance(light.Value):F2}");
    }

    /// <summary>Rasterises the glow layer on a transparent field.</summary>
    private static byte[] RenderGlow(
        int width,
        int height,
        int radius,
        IReadOnlyList<WidgetGlowSegment> segments,
        bool highlight)
    {
        var layer = new WidgetGlowLayer
        {
            Width = width,
            Height = height,
            CardCornerRadius = radius,
            EdgeHighlightOpacity = highlight ? WidgetGlowLayer.DefaultEdgeHighlightOpacity : 0,
            Segments = segments,
        };

        layer.Measure(new Size(width, height));
        layer.Arrange(new Rect(0, 0, width, height));

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(layer);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        var buffer = Marshal.AllocHGlobal(pixels.Length);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), buffer, pixels.Length, stride);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return pixels;
    }

    /// <summary>Alpha of a pixel in a BGRA capture.</summary>
    private static byte Alpha(byte[] pixels, int width, int x, int y) =>
        pixels[(((y * width) + x) * 4) + 3];

    private static string Describe(IReadOnlyList<WidgetGlowSegment> segments) =>
        segments.Count == 0
            ? "nothing"
            : string.Join(", ", segments.Select(s =>
                $"{s.Edge} {s.Start:P0}+{s.Length:P0}"));

    private static bool CanLoadSettingsWindow()
    {
        try
        {
            _ = new SettingsWindow();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// True for a brush that paints nothing at all, which is what an overlay
    /// lying on top of content must use.
    /// </summary>
    private static bool IsFullyTransparent(IBrush? brush) =>
        brush switch
        {
            null => true,
            ISolidColorBrush solid => solid.Color.A == 0,

            // Anything that is not a plain solid colour is treated as opaque
            // enough to obscure the content underneath.
            _ => false,
        };

    /// <summary>
    /// True when the second rectangle lies inside the first, allowing for the
    /// rounding that layout arithmetic introduces.
    /// </summary>
    private static bool IsContainedIn(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5
        && inner.Top >= outer.Top - 0.5
        && inner.Right <= outer.Right + 0.5
        && inner.Bottom <= outer.Bottom + 0.5;

    private static string DescribeBrush(IBrush? brush) =>
        brush switch
        {
            null => "no background",
            ISolidColorBrush solid => $"#{(uint)((solid.Color.A << 24) | (solid.Color.R << 16) | (solid.Color.G << 8) | solid.Color.B):X8}",
            _ => brush.ToString() ?? "unknown",
        };

    /// <summary>Shrinks a rectangle by the given inset on every side.</summary>
    private static PixelRect Inset(PixelRect rect, int inset) =>
        new(
            new PixelPoint(rect.X + inset, rect.Y + inset),
            new PixelSize(
                Math.Max(1, rect.Width - (inset * 2)),
                Math.Max(1, rect.Height - (inset * 2))));
}
