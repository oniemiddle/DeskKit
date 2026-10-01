using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Platform;
using DeskKit.Platform.Windows;
using DeskKit.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeskKit.App.Diagnostics;

/// <summary>
/// Automated verification of the desktop-layer pinning rules, run against a
/// real desktop with <c>DeskKit.App --selftest</c>.
/// <para>
/// The behaviour that matters here — the widget sits above the wallpaper and
/// below every ordinary window, and survives "Show Desktop" — cannot be proven
/// with a unit test, and a driver-dependent rendering failure (a widget that
/// paints as a solid black rectangle) is invisible to a human reading logs.
/// So the probe window is shown, exercised through every code path the shell
/// uses to hide a window, and photographed with <c>BitBlt</c> to confirm that
/// its rounded corner really is see-through.
/// </para>
/// <para>
/// The process is a GUI executable, so the report is written to a file; the
/// path is printed when <c>--out</c> is not supplied.
/// </para>
/// </summary>
internal sealed class DesktopLayerSelfTest
{
    private const string CardColorHex = "#FF2563EB";

    private static readonly Color CardColor = Color.Parse(CardColorHex);

    /// <summary>
    /// Classes that legitimately live below a desktop widget. Anything else
    /// that is visible and has a size sitting below us means the widget is not
    /// really pinned to the bottom.
    /// </summary>
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Button",
        "SysShadow",
        "TaskListThumbnailWnd",
        "Windows.UI.Core.CoreWindow",
        "ApplicationManager_DesktopShellWindow",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "ForegroundStaging",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "EdgeUiInputTopWndClass",
        "ExplorerTAP",
        "OffscreenParentWindow",
    };

    private readonly StringBuilder _report = new();
    private readonly string _outputPath;

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private WidgetWindow? _window;
    private bool _foundFreeSpot = true;
    private int _checks;
    private int _failures;

    private DesktopLayerSelfTest(string outputPath) => _outputPath = outputPath;

    public static DesktopLayerSelfTest? Requested { get; set; }

    public static bool IsRequested(string[] args) =>
        args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));

    public static DesktopLayerSelfTest FromArgs(string[] args)
    {
        var index = Array.FindIndex(args, a =>
            string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));

        var path = index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DeskKit",
                "selftest-report.txt");

        return new DesktopLayerSelfTest(path);
    }

    public void Begin(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        Dispatcher.UIThread.Post(RunSafely, DispatcherPriority.Background);
    }

    private async void RunSafely()
    {
        try
        {
            await RunAsync();
        }
        catch (Exception ex)
        {
            Fail("self-test completed without crashing", ex.ToString());
        }
        finally
        {
            Finish();
        }
    }

    private async Task RunAsync()
    {
        Section("environment");
        Note($"os              : {Environment.OSVersion.VersionString}");
        Note($"64-bit process  : {Environment.Is64BitProcess}");
        Note($"date            : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Check("running on Windows", OperatingSystem.IsWindows());

        if (!OperatingSystem.IsWindows())
            return;

        var layer = new WindowsDesktopLayerService();
        Check("desktop-layer service reports support", layer.IsSupported);

        var window = CreateProbeWindow(layer);
        _window = window;
        window.Show();

        // Give the compositor time to produce the first frame.
        await Delay(1500);

        var hwnd = window.Handle;
        Check("widget window has a native handle", hwnd != IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
            return;

        Note($"window handle   : 0x{hwnd.ToInt64():X}");
        Note($"render scaling  : {window.RenderScaling}");

        foreach (var screen in window.Screens.All)
        {
            Note($"screen          : bounds={screen.Bounds} workingArea={screen.WorkingArea} "
                 + $"scaling={screen.Scaling} primary={ReferenceEquals(screen, window.Screens.Primary)}");
        }

        await CheckRenderingAsync(layer, hwnd);
        CheckZOrder(hwnd);
        CheckStyles(hwnd);
        await CheckShowDesktopResistanceAsync(layer, hwnd);
        await CheckMoveAndResizeAsync(layer, window, hwnd);
        await CheckDragTrackingAsync(window, hwnd);
        await CheckWidgetShellAsync();
    }

    private WidgetWindow CreateProbeWindow(IDesktopLayerService layer)
    {
        var window = new WidgetWindow(layer)
        {
            AcceptsKeyboardFocus = false,
            CardBackground = new SolidColorBrush(CardColor),
            CardMargin = new Thickness(32),
            CardCornerRadius = new CornerRadius(14),
            WidgetContent = new TextBlock
            {
                Text = "DeskKit desktop-layer probe",
                Foreground = Brushes.White,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 300,
            Height = 180,
        };

        var (spot, foundFreeSpot) = FindUncoveredSpot(window, new PixelSize(300, 180));
        window.Position = spot;
        _foundFreeSpot = foundFreeSpot;

        return window;
    }

    /// <summary>
    /// Finds a spot on the primary screen that no visible window covers, so the
    /// screenshot checks photograph the widget rather than whatever happened to
    /// be in front of it.
    /// </summary>
    private static (PixelPoint Point, bool Found) FindUncoveredSpot(Window window, PixelSize size)
    {
        var area = window.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        var mine = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        var obstacles = DesktopDiagnostics.GetTopLevelWindows()
            .Where(w => w.IsVisible
                        && w.HasSize
                        && w.Handle != mine
                        && !ShellWindowClasses.Contains(w.ClassName))
            .Select(w => w.Rect)
            .ToList();

        const int step = 40;
        for (var y = area.Y + 40; y + size.Height + 40 <= area.Bottom; y += step)
        {
            for (var x = area.X + 40; x + size.Width + 40 <= area.Right; x += step)
            {
                var candidate = new PixelRect(x, y, size.Width, size.Height);
                if (!obstacles.Any(o => o.Intersects(candidate)))
                    return (new PixelPoint(x, y), true);
            }
        }

        return (new PixelPoint(area.X + 80, area.Y + 80), false);
    }

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
        Check(
            "showing again is visible and re-pinned to the bottom",
            DesktopDiagnostics.IsVisible(hwnd) && CountOffendersBelow(hwnd) == 0,
            $"visible={DesktopDiagnostics.IsVisible(hwnd)}");
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

    private static int CountOffendersBelow(IntPtr hwnd) =>
        DesktopDiagnostics.GetWindowsBelow(hwnd)
            .Count(w => w.IsVisible && w.HasSize && !ShellWindowClasses.Contains(w.ClassName));

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
    /// Drives the real shell against a throwaway configuration directory: a
    /// first run must seed a widget, pin its window, write the file, and restore
    /// the same placement on the next start.
    /// </summary>
    private async Task CheckWidgetShellAsync()
    {
        Section("8. widget shell end to end");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var configStore = new ConfigStore(directory);
        var registry = new WidgetRegistry();

        foreach (var provider in BuiltInWidgets.CreateProviders(new NullShellIconLoader()))
            registry.Register(provider);

        using var tickService = new TickService();
        var layer = new WindowsDesktopLayerService();

        var shell = new WidgetShell(
            configStore,
            registry,
            layer,
            new NullAutoStartService(),
            tickService,
            new ThemeService(),
            NullLogger<WidgetShell>.Instance);

        try
        {
            shell.Start();
            await Delay(1500);

            Check("a first run seeds exactly one widget", shell.Widgets.Count == 1,
                $"count={shell.Widgets.Count}");

            Check("the configuration file is written on first run",
                File.Exists(configStore.FilePath), configStore.FilePath);

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

            var seeded = runtime.Placement;

            // Restart against the same directory.
            shell.Dispose();
            await Delay(300);

            var reloaded = new ConfigStore(directory).Load();

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

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temporary directory is not worth failing over.
            }
        }
    }

    private static (byte B, byte G, byte R)? Sample(byte[]? capture, PixelRect rect, PixelPoint point)
    {
        if (capture is null)
            return null;

        var x = point.X - rect.X;
        var y = point.Y - rect.Y;
        if (x < 0 || y < 0 || x >= rect.Width || y >= rect.Height)
            return null;

        return DesktopDiagnostics.PixelAt(capture, rect.Width, x, y);
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

    private static Task Delay(int milliseconds) => Task.Delay(milliseconds);

    private void Section(string title)
    {
        _report.AppendLine();
        _report.AppendLine($"== {title} ==");
    }

    private void Note(string message) => _report.AppendLine($"   {message}");

    private void Check(string description, bool passed, string? detail = null)
    {
        _checks++;
        if (!passed)
            _failures++;

        var detailText = detail is null ? string.Empty : $"  [{detail}]";
        _report.AppendLine($"   {(passed ? "PASS" : "FAIL")}  {description}{detailText}");
    }

    private void Fail(string description, string detail) => Check(description, false, detail);

    private void Finish()
    {
        try
        {
            if (_window is not null)
            {
                _window.DesktopLayer.Detach(_window);
                _window.Close();
                _window = null;
            }
        }
        catch (Exception ex)
        {
            _report.AppendLine($"   warning: cleanup failed: {ex.Message}");
        }

        _report.AppendLine();
        _report.AppendLine(_failures == 0
            ? $"RESULT: PASS ({_checks} checks)"
            : $"RESULT: FAIL ({_failures} of {_checks} checks failed)");
        _report.AppendLine();

        var text = _report.ToString();

        try
        {
            var directory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_outputPath, text, Encoding.UTF8);
        }
        catch
        {
            // Nothing useful to do; the exit code still carries the result.
        }

        try
        {
            Console.WriteLine(text);
        }
        catch
        {
            // GUI subsystem processes have no console.
        }

        _desktop?.Shutdown(_failures == 0 ? 0 : 1);
    }
}
