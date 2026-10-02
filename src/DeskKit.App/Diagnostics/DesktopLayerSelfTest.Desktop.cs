using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DeskKit.App.Views;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Platform;
using DeskKit.Platform.Windows;
using DeskKit.Runtime;
using DeskKit.Runtime.Views;

namespace DeskKit.App.Diagnostics;

// The desktop layer itself: pinning, z-order, window styles, and the drag and resize loops.

internal sealed partial class DesktopLayerSelfTest
{
    private async Task CheckRenderingAsync(IDesktopLayerService layer, IntPtr hwnd)
    {
        Section("1. rendering and per-pixel transparency");

        if (!DesktopDiagnostics.TryGetWindowRect(hwnd, out var rect))
        {
            Fail("widget window rect is readable", "GetWindowRect failed");
            return;
        }

        Note($"window rect     : {rect}");
        if (!_foundFreeSpot)
        {
            Note("placement       : no uncovered area on the primary screen, so the widget is "
                 + "behind other windows; the rendering probe raises it temporarily instead");
        }

        NoteOccluders(hwnd, rect);

        var cardPoint = new PixelPoint(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

        // The corner sample sits in the transparent margin, ~30px diagonally
        // away from the rounded card, which is outside its drop shadow.
        var cornerPoint = new PixelPoint(rect.X + 1, rect.Y + 1);

        var window = _window!;
        (byte B, byte G, byte R)? cardVisible;
        (byte B, byte G, byte R)? cornerVisible;
        (byte B, byte G, byte R)? cardHidden;
        (byte B, byte G, byte R)? cornerHidden;

        // Photograph the widget from above. Z-order has no effect on how a
        // window renders, and this is the only way to see its pixels when the
        // desktop happens to be covered by other windows. Pinning is suspended
        // so the z-order change is not immediately undone.
        using (layer.SuspendPinning(window))
        {
            DesktopDiagnostics.SetTopmost(hwnd, true);
            await Delay(400);

            var visibleCapture = DesktopDiagnostics.CaptureBgra(rect);
            cardVisible = Sample(visibleCapture, rect, cardPoint);
            cornerVisible = Sample(visibleCapture, rect, cornerPoint);

            DesktopDiagnostics.SetTopmost(hwnd, false);
            window.Hide();
            await Delay(300);

            var hiddenCapture = DesktopDiagnostics.CaptureBgra(rect);
            cardHidden = Sample(hiddenCapture, rect, cardPoint);
            cornerHidden = Sample(hiddenCapture, rect, cornerPoint);

            window.Show();
        }

        layer.Reassert(window);
        await Delay(300);

        Check("widget is back at the bottom after the rendering probe",
            CountOffendersBelow(hwnd) == 0);

        if (cardVisible is null || cornerVisible is null || cardHidden is null || cornerHidden is null)
        {
            Fail("screen capture succeeded", "BitBlt into a DIB section failed");
            return;
        }

        Note($"card   px visible/hidden : {Hex(cardVisible.Value)} / {Hex(cardHidden.Value)}");
        Note($"corner px visible/hidden : {Hex(cornerVisible.Value)} / {Hex(cornerHidden.Value)}");

        // Because the widget is per-pixel transparent, the corner must look the
        // same whether the widget is shown or hidden; the card centre must not.
        // That makes the check independent of the wallpaper colour and of the
        // drop shadow.
        Check(
            "card centre paints the card colour (not a black rectangle)",
            Near(cardVisible.Value, CardColor, 30) && !Near(cardVisible.Value, cardHidden.Value, 20),
            $"expected ~{CardColorHex}");

        Check(
            "rounded corner is see-through (widget is not an opaque box)",
            Near(cornerVisible.Value, cornerHidden.Value, 20) && !Near(cornerVisible.Value, CardColor, 30),
            "corner pixel must equal the desktop pixel and must not be the card colour");
    }

    private void CheckZOrder(IntPtr hwnd)
    {
        Section("2. z-order");

        var below = DesktopDiagnostics.GetWindowsBelow(hwnd);
        foreach (var info in below.Take(14))
        {
            Note($"  below: {info.ClassName,-32} \"{Shorten(info.Title)}\" " +
                 $"visible={info.IsVisible} minimised={info.IsMinimized} rect={info.Rect}");
        }

        var offenders = below
            .Where(w => w.IsVisible && w.HasSize && !ShellWindowClasses.Contains(w.ClassName))
            .ToList();

        Check(
            "nothing but shell/desktop windows sit below the widget",
            offenders.Count == 0,
            offenders.Count == 0
                ? $"{below.Count} window(s) below, all shell/desktop"
                : string.Join("; ", offenders.Select(o => $"{o.ClassName} \"{Shorten(o.Title)}\"")));
    }

    private void CheckStyles(IntPtr hwnd)
    {
        Section("3. window styles");

        Check("no taskbar button (WS_EX_APPWINDOW clear)",
            !DesktopDiagnostics.HasAppWindowStyle(hwnd));

        Check("clicking does not steal focus (WS_EX_NOACTIVATE set)",
            DesktopDiagnostics.HasNoActivateStyle(hwnd));

        Check("per-pixel transparency path is active (WS_EX_NOREDIRECTIONBITMAP set)",
            DesktopDiagnostics.UsesNoRedirectionBitmap(hwnd));

        Note($"WS_EX_TOOLWINDOW : {DesktopDiagnostics.HasToolWindowStyle(hwnd)} "
             + "(Alt+Tab exclusion comes from the window having a hidden owner)");
    }

    private async Task CheckShowDesktopResistanceAsync(IDesktopLayerService layer, IntPtr hwnd)
    {
        Section("4. show-desktop and minimise resistance");

        var attacks = new (string Name, Action Trigger)[]
        {
            ("Win+D / SC_MINIMIZE", () => DesktopDiagnostics.SendSysCommand(hwnd, DesktopLayerPolicy.ScMinimize)),
            ("taskbar Show Desktop / SC_DESKTOP", () => DesktopDiagnostics.SendSysCommand(hwnd, DesktopLayerPolicy.ScDesktop)),
            ("Win+Up / SC_MAXIMIZE", () => DesktopDiagnostics.SendSysCommand(hwnd, DesktopLayerPolicy.ScMaximize)),
            ("ShowWindow(SW_SHOWMINIMIZED)", () => DesktopDiagnostics.SimulateShowMinimized(hwnd)),
            ("SWP_HIDEWINDOW (hide request)", () => DesktopDiagnostics.SimulateRaiseAndHide(hwnd)),
            ("raise-to-top request", () => DesktopDiagnostics.SimulateRaiseToTop(hwnd)),
        };

        foreach (var (name, trigger) in attacks)
        {
            trigger();
            await Delay(120);

            var visible = DesktopDiagnostics.IsVisible(hwnd);
            var minimized = DesktopDiagnostics.IsMinimized(hwnd);
            var maximized = DesktopDiagnostics.IsMaximized(hwnd);
            var stillBottom = CountOffendersBelow(hwnd) == 0;

            Check(
                $"survives: {name}",
                visible && !minimized && !maximized && stillBottom,
                $"visible={visible} minimised={minimized} maximised={maximized} bottom={stillBottom}");
        }

        Section("5. intentional hide and show");

        layer.SetVisible(_window!, false);
        await Delay(200);
        Check("an intentional hide is honoured", !DesktopDiagnostics.IsVisible(hwnd));

        layer.SetVisible(_window!, true);
        await Delay(300);
        var offenders = CountOffendersBelow(hwnd);
        Check(
            "showing again is visible and re-pinned to the bottom",
            DesktopDiagnostics.IsVisible(hwnd) && offenders == 0,
            $"visible={DesktopDiagnostics.IsVisible(hwnd)} offendersBelow={offenders}");
    }

    private async Task CheckMoveAndResizeAsync(
        IDesktopLayerService layer, WidgetWindow window, IntPtr hwnd)
    {
        Section("6. moving, resizing and the icon-rect guard");

        window.Position = new PixelPoint(window.Position.X + 28, window.Position.Y + 28);
        await Delay(250);
        Check("moving keeps the widget pinned to the bottom", CountOffendersBelow(hwnd) == 0,
            $"position={window.Position}");

        window.Width = 240;
        window.Height = 150;
        await Delay(250);
        layer.SyncNormalSize(window);
        Check("resizing keeps the widget pinned to the bottom", CountOffendersBelow(hwnd) == 0,
            $"size={window.Width}x{window.Height}");

        DesktopDiagnostics.SimulateResize(hwnd, 140, 24);
        await Delay(200);

        var collapsed = DesktopDiagnostics.TryGetWindowRect(hwnd, out var rect) && rect.Height <= 32;
        Check("a collapse to the caption icon rect is refused", !collapsed,
            DesktopDiagnostics.TryGetWindowRect(hwnd, out var after) ? $"height={after.Height}" : "rect unreadable");
    }

    /// <summary>
    /// Replays the real drag loop against a real window, using the same
    /// coordinate conversions the pointer handler uses.
    /// <para>
    /// This is the end-to-end guard for the drag bug: the window used to derive
    /// its new origin partly from its own current position, so it snapped back
    /// towards where the drag began instead of following the cursor. That only
    /// shows up once the window is actually moving, which is why it is exercised
    /// here rather than only in the unit tests.
    /// </para>
    /// </summary>
    private async Task CheckDragTrackingAsync(WidgetWindow window, IntPtr hwnd)
    {
        Section("7. dragging tracks the cursor");

        // Pick a point inside the widget to grab, and find where the cursor
        // would be in screen coordinates.
        var grabClientPoint = new Point(30, 24);
        var grabCursorScreen = window.PointToScreen(grabClientPoint);
        var startPosition = window.Position;

        var session = WidgetDragSession.Start(grabCursorScreen, startPosition);
        Note($"grab offset     : {session.GrabOffset}");

        Check("pressing the button does not move the widget",
            session.PositionFor(grabCursorScreen) == startPosition,
            $"{startPosition} vs {session.PositionFor(grabCursorScreen)}");

        // Move the cursor, then hold it still and let the drag loop run.
        var cursorNow = new PixelPoint(grabCursorScreen.X + 160, grabCursorScreen.Y + 110);
        var expected = new PixelPoint(cursorNow.X - session.GrabOffset.X, cursorNow.Y - session.GrabOffset.Y);

        var positions = new List<PixelPoint>();
        for (var frame = 0; frame < 6; frame++)
        {
            // The platform delivers the cursor in the window's coordinates; turn
            // that back into a screen position exactly as the handler does.
            var pointerScreen = window.PointToScreen(window.PointToClient(cursorNow));
            var target = session.PositionFor(pointerScreen);

            if (window.Position != target)
                window.Position = target;

            await Delay(90);

            positions.Add(DesktopDiagnostics.TryGetWindowRect(hwnd, out var rect)
                ? new PixelPoint(rect.X, rect.Y)
                : new PixelPoint(int.MinValue, int.MinValue));
        }

        Note($"cursor          : {cursorNow}");
        Note($"window per frame: {string.Join(" ", positions)}");

        Check("the widget ends up exactly under the cursor",
            Math.Abs(positions[^1].X - expected.X) <= 2 && Math.Abs(positions[^1].Y - expected.Y) <= 2,
            $"expected {expected}, got {positions[^1]}");

        // The old implementation alternated between two positions here, which is
        // what a user sees as the widget flashing back to its previous spot.
        Check("a held cursor position stops moving the widget (no oscillation)",
            positions.Skip(1).All(p => p == positions[1]),
            $"frames: {string.Join(" ", positions)}");

        // One pixel of cursor movement must be one pixel of widget movement, not
        // the roughly half-speed tracking the feedback loop produced.
        var onePixel = new PixelPoint(cursorNow.X + 1, cursorNow.Y + 1);
        var pointerForOnePixel = window.PointToScreen(window.PointToClient(onePixel));
        var nudged = session.PositionFor(pointerForOnePixel);

        Check("one pixel of cursor movement moves the widget one pixel",
            Math.Abs(nudged.X - (expected.X + 1)) <= 1 && Math.Abs(nudged.Y - (expected.Y + 1)) <= 1,
            $"expected ({expected.X + 1},{expected.Y + 1}), got {nudged}");

        // Put it back so the later checks start from a known place.
        window.Position = startPosition;
        await Delay(150);
    }

    /// <summary>
    /// Checks that the whole card perimeter is a resize handle except the band
    /// the drag strip owns, and that dragging one actually resizes the window.
    /// </summary>
    private async Task CheckResizeAsync(WidgetWindow window, IntPtr hwnd)
    {
        Section("8. resizing");

        var card = window.CardBounds;
        var midWidth = card.X + (card.Width / 2);
        var midHeight = card.Y + (card.Height / 2);

        Note($"card            : {card}");

        Check("the card's top band is left to the drag strip, not the resize handle",
            window.HitTestResizeEdges(new Point(midWidth, card.Y + 2)) == WidgetEdges.None);

        Check("the left edge resizes",
            window.HitTestResizeEdges(new Point(card.X + 2, midHeight)) == WidgetEdges.Left);

        Check("the right edge resizes",
            window.HitTestResizeEdges(new Point(card.Right - 2, midHeight)) == WidgetEdges.Right);

        Check("the bottom edge resizes",
            window.HitTestResizeEdges(new Point(midWidth, card.Bottom - 2)) == WidgetEdges.Bottom);

        Check("the bottom-right corner resizes both axes",
            window.HitTestResizeEdges(new Point(card.Right - 2, card.Bottom - 2))
                == (WidgetEdges.Right | WidgetEdges.Bottom));

        Check("the top-left corner still resizes, even though the top band does not",
            window.HitTestResizeEdges(new Point(card.X + 2, card.Y + 2))
                == (WidgetEdges.Left | WidgetEdges.Top));

        Check("the middle of the card is not a resize handle",
            window.HitTestResizeEdges(new Point(midWidth, midHeight)) == WidgetEdges.None);

        // Now actually resize, through the same entry points the pointer
        // handlers use.
        var beforeRect = DesktopDiagnostics.TryGetWindowRect(hwnd, out var measured) ? measured : default;
        var beforeCard = window.CardBounds.Size;
        Note($"before          : window={beforeRect} card={beforeCard}");

        var grab = new PixelPoint(beforeRect.X + beforeRect.Width - 4, beforeRect.Y + beforeRect.Height - 4);
        window.BeginResize(WidgetEdges.Right | WidgetEdges.Bottom, grab);
        Check("a resize is in progress once an edge is grabbed", window.IsResizing);

        window.ApplyResize(new PixelPoint(grab.X + 60, grab.Y + 40));
        await Delay(350);

        var afterRect = DesktopDiagnostics.TryGetWindowRect(hwnd, out var resized) ? resized : default;
        var afterCard = window.CardBounds.Size;
        Note($"after           : window={afterRect} card={afterCard}");

        Check("dragging a corner makes the card wider",
            afterCard.Width > beforeCard.Width + 30, $"{beforeCard.Width} -> {afterCard.Width}");

        Check("dragging a corner makes the card taller",
            afterCard.Height > beforeCard.Height + 20, $"{beforeCard.Height} -> {afterCard.Height}");

        Check("a right/bottom resize leaves the window's top-left corner where it was",
            Math.Abs(afterRect.X - beforeRect.X) <= 2 && Math.Abs(afterRect.Y - beforeRect.Y) <= 2,
            $"{beforeRect.X},{beforeRect.Y} -> {afterRect.X},{afterRect.Y}");

        Check("resizing keeps the widget pinned to the bottom",
            CountOffendersBelow(hwnd) == 0);

        window.EndResize();
        Check("the resize ends cleanly", !window.IsResizing);

        // Dragging the left edge inwards must move the origin while the right
        // edge stays put.
        var anchoredRight = afterRect.Right;
        var leftGrab = new PixelPoint(afterRect.X + 2, afterRect.Y + (afterRect.Height / 2));

        window.BeginResize(WidgetEdges.Left, leftGrab);
        window.ApplyResize(new PixelPoint(leftGrab.X + 40, leftGrab.Y));
        await Delay(350);

        var narrowed = DesktopDiagnostics.TryGetWindowRect(hwnd, out var narrow) ? narrow : default;
        Note($"after left drag : window={narrowed}");

        Check("dragging the left edge keeps the opposite edge anchored",
            Math.Abs(narrowed.Right - anchoredRight) <= 2,
            $"right edge {anchoredRight} -> {narrowed.Right}");

        window.EndResize();

        // Never let the widget be shrunk below its own minimum.
        var tinyGrab = new PixelPoint(narrowed.X + 2, narrowed.Y + (narrowed.Height / 2));
        window.BeginResize(WidgetEdges.Left, tinyGrab);
        window.ApplyResize(new PixelPoint(tinyGrab.X + 5000, tinyGrab.Y));
        await Delay(350);

        var minimum = DesktopDiagnostics.TryGetWindowRect(hwnd, out var clamped) ? clamped : default;
        var minimumCard = window.CardBounds.Size;
        Note($"at minimum      : window={minimum} card={minimumCard} min={window.MinWidth}x{window.MinHeight}");

        Check("the widget cannot be shrunk below its minimum",
            minimum.Width >= window.MinWidth - 2 && minimumCard.Width > 0,
            $"width={minimum.Width} min={window.MinWidth}");

        window.EndResize();
        await Delay(150);
    }

    private void NoteOccluders(IntPtr hwnd, PixelRect rect)
    {
        var occluders = DesktopDiagnostics.GetWindowsAbove(hwnd)
            .Where(w => w.IsVisible && w.HasSize && w.Rect.Intersects(rect))
            .ToList();

        if (occluders.Count == 0)
        {
            Note("occluders       : none, the widget is unobstructed on screen");
            return;
        }

        Note($"occluders       : {occluders.Count} window(s) cover the widget while it is pinned");
        foreach (var info in occluders.Take(5))
        {
            Note($"  above: {info.ClassName,-32} \"{Shorten(info.Title)}\" rect={info.Rect}");
        }
    }

    private static bool Near((byte B, byte G, byte R) a, (byte B, byte G, byte R) b, int tolerance) =>
        Math.Abs(a.R - b.R) <= tolerance
        && Math.Abs(a.G - b.G) <= tolerance
        && Math.Abs(a.B - b.B) <= tolerance;

    private static bool Near((byte B, byte G, byte R) pixel, Color color, int tolerance) =>
        Near(pixel, (color.B, color.G, color.R), tolerance);

    private static string Hex((byte B, byte G, byte R) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string Shorten(string value) =>
        value.Length <= 28 ? value : value[..28];
}
