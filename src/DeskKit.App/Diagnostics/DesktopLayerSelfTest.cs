using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using DeskKit.App.Localization;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Platform;
using DeskKit.Platform.Windows;
using DeskKit.Widgets;
using DeskKit.Widgets.Clock;
using DeskKit.Widgets.Localization;
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
        await CheckResizeAsync(window, hwnd);
        await CheckWidgetShellAsync();
        await CheckDragHandleChromeAsync();
        CheckGlowPixels();
        await CheckWindowMaterialAsync();
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
    }

    /// <summary>
    /// Verifies that a widget which cannot be shown where it was saved is moved for
    /// this session only.
    /// <para>
    /// Normalising a placement and letting that reach the file is what used to make
    /// a layout degrade every time a laptop was undocked: the position the user
    /// chose was overwritten by a position their display layout had forced, on a
    /// plain start-and-exit with no user action at all. The window still has to be
    /// reachable — the point is only that the adjustment is not mistaken for intent.
    /// </para>
    /// </summary>
    private async Task CheckPlacementAuthorshipAsync()
    {
        Section("15. placement authorship");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-placement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        // Far outside any monitor, so normalisation has to do something.
        const int offScreenX = 40000;
        const int offScreenY = 40000;

        var configPath = Path.Combine(directory, AppPaths.ConfigFileName);
        File.WriteAllText(
            configPath,
            $$"""
            {
              "version": 1,
              "settings": { "theme": "System", "showTrayIcon": true, "widgetsVisible": true, "language": "System" },
              "widgets": [
                {
                  "instanceId": "offscreen",
                  "widgetId": "clock",
                  "enabled": true,
                  "x": {{offScreenX}},
                  "y": {{offScreenY}},
                  "width": 260,
                  "height": 130,
                  "settings": {}
                }
              ]
            }
            """);

        var registry = new WidgetRegistry();
        foreach (var provider in BuiltInWidgets.CreateProviders(new NullShellIconLoader()))
            registry.Register(provider);

        using var tickService = new TickService();

        var shell = new WidgetShell(
            new ConfigStore(directory),
            registry,
            new WindowsDesktopLayerService(),
            new NullAutoStartService(),
            tickService,
            new ThemeService(),
            NullLogger<WidgetShell>.Instance);

        try
        {
            shell.Start();
            await Delay(1500);

            var runtime = shell.Runtimes.FirstOrDefault();
            if (runtime is null)
            {
                Fail("the off-screen widget was still created", "no runtime");
                return;
            }

            var window = runtime.Window;
            Note($"stored          : {runtime.Placement.X}, {runtime.Placement.Y}");
            Note($"window placed at: {window.Position}");
            Note($"screens         : {string.Join(" | ", shell.Screens.Select(s => s.ToString()))}");

            var measured = DesktopDiagnostics.TryGetWindowRect(window.Handle, out var rect);
            foreach (var screen in shell.Screens)
            {
                Note($"  screen {screen}: visibleCorner(rect)="
                     + screen.HasVisibleCorner(
                         rect.X, rect.Y,
                         PlacementNormalizer.RequiredVisibleWidth,
                         PlacementNormalizer.RequiredVisibleHeight));
            }

            // Half one: it is still usable, which is what normalising is for.
            var onScreen = measured && shell.Screens.Any(s => s.HasVisibleCorner(
                rect.X, rect.Y,
                PlacementNormalizer.RequiredVisibleWidth,
                PlacementNormalizer.RequiredVisibleHeight));

            Check("a widget saved off every monitor is brought back into view",
                onScreen,
                $"measured={measured} rect={rect} screens={shell.Screens.Count}");

            Check("the window is placed somewhere other than where it was stored",
                window.Position.X != offScreenX || window.Position.Y != offScreenY,
                $"window={window.Position}");
        }
        finally
        {
            // Dispose is what persists, and it does so unconditionally — which is
            // exactly how the adjustment used to reach the file.
            shell.Dispose();
            await Delay(300);
        }

        // Half two: the file still holds what the user had, so the monitor coming
        // back restores the layout instead of finding it already overwritten.
        var saved = new ConfigStore(directory).Load();
        var placement = saved.Widgets.FirstOrDefault();

        Check("the stored position survives the session untouched",
            placement is not null && placement.X == offScreenX && placement.Y == offScreenY,
            placement is null
                ? "no widget was saved"
                : $"stored now {placement.X}, {placement.Y}, expected {offScreenX}, {offScreenY}");

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
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
            && LinguaText.Of(AppLanguage.Instance.Tray_Exit).Length > 0,
            $"widget={WidgetText.Value(WidgetText.ClockName)} " +
            $"tray={LinguaText.Of(AppLanguage.Instance.Tray_Exit)}");

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

    private readonly record struct LanguageSample(string WidgetName, string WindowTitle);

    private static LanguageSample CollectStrings(WidgetRegistry registry) =>
        new(
            WidgetText.Value(
                registry.Find(ClockWidgetProvider.WidgetId)?.Descriptor.DisplayName
                ?? ClockWidgetProvider.WidgetId),
            LinguaText.Of(AppLanguage.Instance.App_Title));

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

    private static Color? ResolveForeground(Application application, ThemeVariant variant) =>
        application.TryFindResource("WidgetPrimaryForeground", variant, out var value)
        && value is ISolidColorBrush brush
            ? brush.Color
            : null;

    private static double Luminance(Color colour) =>
        ((0.2126 * colour.R) + (0.7152 * colour.G) + (0.0722 * colour.B)) / 255.0;

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

    private static string Describe(IReadOnlyList<WidgetGlowSegment> segments) =>
        segments.Count == 0
            ? "nothing"
            : string.Join(", ", segments.Select(s =>
                $"{s.Edge} {s.Start:P0}+{s.Length:P0}"));

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

        var registry = new WidgetRegistry();
        foreach (var provider in BuiltInWidgets.CreateProviders(new NullShellIconLoader()))
            registry.Register(provider);

        using var tickService = new TickService();

        var shell = new WidgetShell(
            new ConfigStore(directory),
            registry,
            new WindowsDesktopLayerService(),
            new NullAutoStartService(),
            tickService,
            new ThemeService(),
            NullLogger<WidgetShell>.Instance);

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

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

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
