using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Data;
using DeskKit.Core.Services;
using DeskKit.Platform;
using DeskKit.Platform.Windows;
using Microsoft.Data.Sqlite;
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
internal sealed partial class DesktopLayerSelfTest
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
            : Path.Combine(AppPaths.DataDirectory, "selftest-report.txt");

        return new DesktopLayerSelfTest(path);
    }

    /// <summary>Starts a copy of this process that writes until something kills it.</summary>
    private const string WriteLoopSwitch = "--stress-write";

    /// <summary>
    /// True when this process was started to write in a loop, for the kill test.
    /// </summary>
    /// <remarks>
    /// Only the presence of the switch is checked here. Whether the location is
    /// acceptable is decided by <see cref="RunWriteLoop"/>, which refuses rather than
    /// returning, so that a rejected location can never fall through to starting the
    /// application — and writing to the real database — instead.
    /// </remarks>
    public static bool IsWriteLoopRequested(string[] args) =>
        args.Any(argument => string.Equals(argument, WriteLoopSwitch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Writes to the database named after the switch, in a loop, until the process is
    /// killed. Returns only to refuse.
    /// </summary>
    /// <remarks>
    /// Both fields written in a round carry that round's number, so a save that was
    /// applied in part — the row moved but the theme not, say — is visible to the test
    /// afterwards as the two disagreeing.
    /// </remarks>
    public static int RunWriteLoop(string[] args)
    {
        var index = Array.FindIndex(args, argument =>
            string.Equals(argument, WriteLoopSwitch, StringComparison.OrdinalIgnoreCase));

        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"{WriteLoopSwitch} needs the database to write to.");
            return 2;
        }

        var databasePath = Path.GetFullPath(args[index + 1]);
        var temporary = Path.GetFullPath(Path.GetTempPath());

        if (!databasePath.StartsWith(temporary, StringComparison.OrdinalIgnoreCase))
        {
            // This mode exists to be killed in the middle of a write, so a location that
            // matters is refused.
            Console.Error.WriteLine($"Refusing to write to {databasePath}: it is not under {temporary}.");
            return 3;
        }

        var store = new StateStore(databasePath, Path.Combine(temporary, "deskkit-no-legacy"));

        // Three markers, so that a run which stops before writing anything says where it
        // stopped rather than only that it stopped.
        File.WriteAllText(databasePath + ".entered", $"pid={Environment.ProcessId} args={string.Join('|', args)}");

        try
        {
            store.Load();

            File.WriteAllText(
                databasePath + ".loaded",
                $"outcome={store.LoadReport.Outcome} problems={store.LoadReport.Problems.Count}");

            var first = store.Save(State(0));

            File.WriteAllText(
                databasePath + ".saved",
                $"firstSave={first.Outcome}/{first.RowsWritten}");

            for (var round = 1; ; round++)
                store.Save(State(round));
        }
        catch (Exception ex)
        {
            File.WriteAllText(databasePath + ".failed", ex.ToString());
            return 4;
        }
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
        await CheckResizeAsync(window, hwnd);
        await CheckWidgetShellAsync();
        await CheckDragHandleChromeAsync();
        CheckGlowPixels();
        await CheckWindowMaterialAsync();
    }

    /// <summary>Everything a process has said on one of its streams so far.</summary>
    private static string Read(StreamReader reader)
    {
        try
        {
            return reader.ReadToEnd().Trim();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Verifies that an installation whose whole layout is still in the old JSON file
    /// is brought into the database, and that the trip costs nothing.
    /// </summary>
    /// <remarks>
    /// The old file is the only copy of the layout, so this is the one place where
    /// reading it wrongly would lose real user data. It is therefore done against a
    /// temporary directory and checked from the database and from the file system,
    /// rather than from the object graph that wrote it.
    /// </remarks>
    private static WidgetShell CreateShell(
        string directory,
        StateStore? store = null,
        WidgetRegistry? registry = null,
        IDesktopLayerService? desktopLayer = null,
        INoticePresenter? notices = null,
        LanguageService? language = null,
        IWidgetMessageBus? messages = null) =>
        new(
            store ?? new StateStore(Path.Combine(directory, AppPaths.DatabaseFileName), directory),
            registry ?? BuiltInRegistry(),
            desktopLayer ?? new WindowsDesktopLayerService(),
            new NullAutoStartService(),
            new TickService(),
            new ThemeService(),
            new NullWindowMaterialService(),
            language ?? new LanguageService(),
            notices ?? NullNoticePresenter.Instance,
            messages ?? new WidgetMessageBus(),
            NullLogger<WidgetShell>.Instance);

    /// <summary>
    /// Verifies that a database which was being written when its process was killed can
    /// be opened by the next run, and holds a whole number of saves.
    /// </summary>
    /// <remarks>
    /// This is the part of the durability claim that is measured rather than quoted: a
    /// real process is killed in the middle of writing, and the database is then opened,
    /// checked, and read for a save that was applied in part. What it does not cover is
    /// the machine losing power — nothing running here can arrange that — which is what
    /// the flush on commit is for, and is asserted on its own.
    /// </remarks>
    private void RemoveTemporary(string directory)
    {
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                break;
            }
            catch (IOException) when (attempt < 4)
            {
                // Something outside this process can hold the files briefly.
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                Note($"the temporary folder survived: {directory}");
                break;
            }
        }
    }

    /// <summary>Runs one query against the database and returns its first value.</summary>
    private static object? Query(string databasePath, string sql)
    {
        using var connection = new SqliteConnection(DatabaseOptions.ConnectionStringFor(databasePath));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    private readonly record struct LanguageSample(string WidgetName, string WindowTitle);

    private static Color? ResolveForeground(Application application, ThemeVariant variant) =>
        application.TryFindResource("WidgetPrimaryForeground", variant, out var value)
        && value is ISolidColorBrush brush
            ? brush.Color
            : null;

    private static double Luminance(Color colour) =>
        ((0.2126 * colour.R) + (0.7152 * colour.G) + (0.0722 * colour.B)) / 255.0;

    private WidgetWindow CreateProbeWindow(IDesktopLayerService layer)
    {
        var window = new WidgetWindow(layer)
        {
            AcceptsKeyboardFocus = false,
            CardBackground = new SolidColorBrush(CardColor),

            // Matches a real widget, so the reported geometry is comparable.
            CardMargin = new Thickness(WidgetWindow.GlowMargin),
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

    private static int CountOffendersBelow(IntPtr hwnd) =>
        DesktopDiagnostics.GetWindowsBelow(hwnd)
            .Count(w => w.IsVisible && w.HasSize && !ShellWindowClasses.Contains(w.ClassName));

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
