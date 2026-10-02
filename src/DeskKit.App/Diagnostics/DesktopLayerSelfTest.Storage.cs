using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DeskKit.App.Localization;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Persistence;
using DeskKit.Platform.Windows;
using DeskKit.Widgets.QuickLaunch;
using Microsoft.Data.Sqlite;
using DeskKit.Runtime;
using DeskKit.Runtime.Views;

namespace DeskKit.App.Diagnostics;

// Stored data: the import, widget settings migrations, degraded storage and the kill test.

internal sealed partial class DesktopLayerSelfTest
{
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

        var widgetsDirectory = Path.Combine(directory, AppPaths.WidgetsDirectoryName);
        Directory.CreateDirectory(widgetsDirectory);

        File.WriteAllText(
            Path.Combine(directory, AppPaths.SettingsFileName),
            $$"""
            {
              "version": 2,
              "settings": { "theme": "System", "showTrayIcon": true, "widgetsVisible": true, "language": "System" }
            }
            """);

        File.WriteAllText(
            Path.Combine(widgetsDirectory, "offscreen.json"),
            $$"""
            {
              "version": 1,
              "order": 0,
              "widgetId": "clock",
              "enabled": true,
              "x": {{offScreenX}},
              "y": {{offScreenY}},
              "width": 260,
              "height": 130,
              "settingsVersion": 1,
              "settings": {}
            }
            """);

        var shell = CreateShell(directory);

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
        var saved = new StateStore(Path.Combine(directory, AppPaths.DatabaseFileName), directory).Load();
        var placement = saved.Widgets.FirstOrDefault();

        Check("the stored position survives the session untouched",
            placement is not null && placement.X == offScreenX && placement.Y == offScreenY,
            placement is null
                ? "no widget was saved"
                : $"stored now {placement.X}, {placement.Y}, expected {offScreenX}, {offScreenY}");

        RemoveTemporary(directory);
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
    private async Task CheckStorageLayoutAsync()
    {
        Section("16. bringing the old configuration into the database");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        // Far outside any monitor, so the session has to move the window into view —
        // and then has to leave the stored position alone anyway.
        const int offScreenX = 40000;
        const int offScreenY = 40000;

        var legacyPath = Path.Combine(directory, AppPaths.LegacyConfigFileName);
        File.WriteAllText(
            legacyPath,
            $$"""
            {
              "version": 1,
              "settings": { "theme": "System", "showTrayIcon": true, "widgetsVisible": true, "language": "System" },
              "widgets": [
                {
                  "instanceId": "legacyclock",
                  "widgetId": "clock",
                  "enabled": true,
                  "x": {{offScreenX}},
                  "y": {{offScreenY}},
                  "width": 260,
                  "height": 130,
                  "settings": { "use24Hour": false }
                }
              ]
            }
            """);

        var before = File.ReadAllBytes(legacyPath);

        var shell = CreateShell(directory);

        try
        {
            shell.Start();
            await Delay(1500);

            Check("the old layout is read", shell.Widgets.Count == 1,
                $"count={shell.Widgets.Count}");
        }
        finally
        {
            shell.Dispose();
            await Delay(300);
        }

        var databasePath = Path.Combine(directory, AppPaths.DatabaseFileName);

        Note($"database        : {(File.Exists(databasePath) ? "created" : "missing")}");
        Note($"old file        : {(File.Exists(legacyPath) ? "left alone" : "gone")}");
        Note($"imported from   : {ImportedFrom(databasePath)}");

        Check("the database is created", File.Exists(databasePath));

        // The old file is the only copy of the layout, so nothing about it is written,
        // renamed or deleted: a build from before the move still finds it where it was.
        Check("the old file is left exactly as it was",
            before.SequenceEqual(File.ReadAllBytes(legacyPath)));

        Check("the import is recorded in the database",
            ImportedFrom(databasePath) is { } recorded
            && recorded.Contains(AppPaths.LegacyConfigFileName, StringComparison.Ordinal),
            ImportedFrom(databasePath) ?? "no record");

        var reloaded = new StateStore(databasePath, directory).Load();
        var widget = reloaded.Widgets.FirstOrDefault();

        Check("the widget's own settings survive the move into the database",
            widget is not null && !new WidgetSettings(widget.Settings).Get("use24Hour", true),
            widget is null ? "no widget" : new WidgetSettings(widget.Settings).Get("use24Hour", true).ToString());

        Check("the stored position survives being moved into view",
            widget is not null && widget.X == offScreenX && widget.Y == offScreenY,
            widget is null ? "no widget" : $"stored {widget.X}, {widget.Y}");

        RemoveTemporary(directory);
    }

    /// <summary>
    /// Verifies that a widget's own settings are brought forward by the widget, with
    /// the shell only carrying the version number.
    /// </summary>
    private async Task CheckWidgetSettingsMigrationAsync()
    {
        Section("17. widget settings migration");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-migrate-" + Guid.NewGuid().ToString("N"));
        var widgetsDirectory = Path.Combine(directory, AppPaths.WidgetsDirectoryName);
        Directory.CreateDirectory(widgetsDirectory);

        File.WriteAllText(
            Path.Combine(directory, AppPaths.SettingsFileName),
            $$"""
            {
              "version": 2,
              "settings": { "theme": "System", "showTrayIcon": true, "widgetsVisible": true, "language": "System" }
            }
            """);

        // A launcher exactly as the version before this one wrote it: the shortcut
        // list in the serializer's default naming, sitting in a camelCase file.
        File.WriteAllText(
            Path.Combine(widgetsDirectory, "launcher.json"),
            """
            {
              "version": 1,
              "order": 0,
              "widgetId": "quick-launch",
              "enabled": true,
              "x": 300,
              "y": 300,
              "width": 300,
              "height": 150,
              "settingsVersion": 1,
              "settings": {
                "items": [
                  { "Name": "Notepad", "Target": "C:\\Windows\\notepad.exe", "Arguments": "", "WorkingDirectory": "" }
                ]
              }
            }
            """);

        var shell = CreateShell(directory);

        try
        {
            shell.Start();
            await Delay(1500);

            Check("the launcher still shows its shortcut", shell.Widgets.Count == 1,
                $"count={shell.Widgets.Count}");
        }
        finally
        {
            shell.Dispose();
            await Delay(300);
        }

        // The migrated settings are what the shell wrote to the database, so they are
        // read back from there. The old file must still hold the shape it arrived in:
        // the import reads it and never writes to it.
        var reloaded = new StateStore(Path.Combine(directory, AppPaths.DatabaseFileName), directory).Load();
        var stored = reloaded.Widgets[0];
        var storedJson = JsonSerializer.Serialize(stored.Settings);

        Note($"stored items    : {storedJson}");
        Note($"old file        : {OldLauncherFile(widgetsDirectory)}");

        Check("the widget migrated its own settings",
            storedJson.Contains("\"name\"", StringComparison.Ordinal)
            && !storedJson.Contains("\"Name\"", StringComparison.Ordinal),
            storedJson);

        Check("the settings version was brought forward", stored.SettingsVersion == 2,
            $"version={stored.SettingsVersion}");

        Check("the old widget file is left in the shape it was written in",
            OldLauncherFile(widgetsDirectory).Contains("\"Name\"", StringComparison.Ordinal));

        // The point of the rewrite is that the shortcuts still work afterwards.
        var items = new WidgetSettings(stored.Settings)
            .Get("items", new List<QuickLaunchItem>());

        Check("the shortcuts are still readable",
            items.Count == 1
            && items[0].Name == "Notepad"
            && items[0].Target == @"C:\Windows\notepad.exe",
            $"count={items.Count}");

        RemoveTemporary(directory);
    }

    /// <summary>
    /// Verifies that a session which cannot open its storage says so, and still leaves
    /// the database it could not read exactly as it found it.
    /// </summary>
    private async Task CheckDegradedStorageNoticeAsync()
    {
        Section("18. degraded storage");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-degraded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        // A file where the database should be. SQLite reports this as "file is not a
        // database": nothing can be read, and nothing may be written over it.
        const string notADatabase = "this is not a database";
        var databasePath = Path.Combine(directory, AppPaths.DatabaseFileName);
        File.WriteAllText(databasePath, notADatabase);

        var before = File.ReadAllBytes(databasePath);

        var notices = new RecordingNoticePresenter();

        var shell = CreateShell(directory, notices: notices);

        try
        {
            // Start is what reads the database, and Dispose is what tries to write it.
            // Both have to leave the file alone.
            shell.Start();
            await Delay(800);
        }
        finally
        {
            shell.Dispose();
            await Delay(300);
        }

        foreach (var notice in notices.Notices)
            Note($"notice          : {notice.Title} / {notice.Message}");

        // Once, not on every attempt to save: the state is known before the first
        // widget is even created.
        var shown = AssertSingleNotice(notices);
        Check("the session that cannot save says so",
            shown is not null
            && shown.Message == AppLanguage.Instance.Notice_Unavailable.CurrentText(),
            shown?.Message ?? "no notice");

        Check("the database that could not be read is untouched",
            before.SequenceEqual(File.ReadAllBytes(databasePath)),
            $"now {File.ReadAllBytes(databasePath).Length} bytes, was {before.Length}");

        RemoveTemporary(directory);
    }

    /// <summary>
    /// Verifies that the notice window really behaves like a notification, and that
    /// it actually paints a surface.
    /// </summary>
    /// <remarks>
    /// The style checks are worth asserting because Avalonia's <c>ShowInTaskbar</c>
    /// and <c>ShowActivated</c> cover less than they sound like they do: a window with
    /// both set still appeared in Alt+Tab until the platform styles were applied.
    /// <para>
    /// The surface checks exist because a notice whose card paints nothing looks
    /// exactly like a notice that is not there, and that failure is invisible to every
    /// other check here — it happened during development, with the text left lying on
    /// the wallpaper. Being on top is deliberately not asserted: on the machine this
    /// was built on no window could be made topmost at all, so the measurement would
    /// say nothing about this window.
    /// </para>
    /// </remarks>
    private async Task CheckNoticeWindowAsync()
    {
        Section("19. notice window");

        var window = new NoticeWindow(
            AppLanguage.Instance.Notice_Title.CurrentText(),
            AppLanguage.Instance.Notice_Unavailable.CurrentText());

        try
        {
            window.Show();
            await Delay(800);

            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var style = handle == IntPtr.Zero ? 0 : DesktopDiagnostics.GetExtendedStyle(handle);

            Note($"handle          : 0x{handle.ToInt64():X}");
            Note($"extended style  : 0x{style:X}");
            Note($"position        : {window.Position} size={window.ClientSize}");

            if (handle == IntPtr.Zero)
            {
                Fail("the notice window has a native handle", "no handle");
                return;
            }

            Check("the notice is not on the taskbar", !DesktopDiagnostics.HasAppWindowStyle(handle));
            Check("the notice is out of Alt+Tab", DesktopDiagnostics.HasToolWindowStyle(handle));
            Check("the notice is never activated", DesktopDiagnostics.HasNoActivateStyle(handle));

            var area = window.Screens.Primary?.WorkingArea ?? default;
            Check("the notice sits in the corner a balloon would use",
                area.Width > 0
                && window.Position.X + window.ClientSize.Width > area.X + area.Width - 40
                && window.Position.Y + window.ClientSize.Height > area.Y + area.Height - 40
                && window.Position.X > area.X + (area.Width / 2),
                $"position={window.Position} client={window.ClientSize} workingArea={area}");

            CheckNoticeSurface(window);
        }
        finally
        {
            window.Close();
            await Delay(200);
        }
    }

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
    private async Task CheckWriteSurvivesBeingKilledAsync()
    {
        Section("20. writing while being killed");

        var directory = Path.Combine(
            Path.GetTempPath(), "deskkit-kill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, AppPaths.DatabaseFileName);
        var executable = Environment.ProcessPath;

        Note($"temp folder     : {Path.GetTempPath()}");
        Note($"writer database : {databasePath}");
        Note($"self            : {executable}");

        if (executable is null)
        {
            Fail("the process can name itself to start a second copy", "no process path");
            return;
        }

        // Three attempts at different moments, because a kill lands wherever it lands.
        var realDatabase = AppPaths.DatabasePath;
        var realBefore = Fingerprint(realDatabase);

        // The self-test process is itself a DeskKit, so anything beyond one is the
        // user's own copy, which is allowed to change the real database while it runs
        // and would make the check below meaningless.
        var othersRunning = Process.GetProcessesByName("DeskKit.App").Length - 1;
        if (othersRunning > 0)
            Note($"another DeskKit is running ({othersRunning}), so the real database may change");

        foreach (var grace in new[] { 20, 60, 120 })
        {
            foreach (var marker in new[] { ".entered", ".loaded", ".saved", ".failed" })
                File.Delete(databasePath + marker);

            using var writer = Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = $"{WriteLoopSwitch} \"{databasePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (writer is null)
            {
                Fail("the writing process starts", "no process");
                return;
            }

            // Waiting for the first commit is what makes this a test at all: a cold
            // process spends its first seconds starting up and building the model, and a
            // kill during that window would say nothing about writing.
            var committing = await WaitForWriter(writer, databasePath + ".saved");

            Check($"[{grace} ms after the first commit] the writer committed something",
                committing, WriterReport(databasePath));

            if (!committing)
            {
                writer.Kill(entireProcessTree: true);
                return;
            }

            // Then a short grace, so the kill lands inside the loop rather than between
            // two rounds.
            await Delay(grace);

            var wasWriting = !writer.HasExited;
            writer.Kill(entireProcessTree: true);
            writer.WaitForExit(5000);

            var complaints = Read(writer.StandardError);
            if (complaints.Length > 0)
                Note($"grace {grace,4} ms : the writer said \"{complaints}\"");

            var store = new StateStore(databasePath, directory);
            var state = store.Load();
            var widget = state.Widgets.FirstOrDefault();
            var theme = state.Settings.Theme;

            Note($"grace {grace,4} ms : wasWriting={wasWriting} theme={theme} x={widget?.X} "
                 + $"outcome={store.LoadReport.Outcome} integrity={IntegrityCheck(databasePath)}");

            Check($"[grace {grace} ms] it was still writing when it was killed", wasWriting);
            Check($"[grace {grace} ms] the database opens afterwards",
                store.LoadReport.Outcome == StoreOutcome.Loaded && !store.LoadReport.HasProblems,
                $"{store.LoadReport.Outcome}, {store.LoadReport.Problems.Count} problems");
            Check($"[grace {grace} ms] an integrity check passes", IntegrityCheck(databasePath) == "ok",
                IntegrityCheck(databasePath) ?? "no answer");

            // The theme and the position are written by the same save, so a save that was
            // applied in part would show up here as the two coming from different rounds.
            var round = theme.StartsWith("round-", StringComparison.Ordinal)
                ? theme["round-".Length..]
                : null;

            Check($"[grace {grace} ms] the last save is a whole one",
                round is not null
                && int.TryParse(round, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && widget is not null
                && widget.X == number,
                $"theme={theme} x={widget?.X}");
        }

        if (othersRunning == 0)
        {
            // The switch refuses a location that is not temporary, and refusing must not
            // fall through to starting the application: a mistake in it would otherwise
            // run a second DeskKit and rewrite the user's own database.
            Check("the real database was not touched by any of this",
                Fingerprint(realDatabase) == realBefore,
                $"{realBefore} became {Fingerprint(realDatabase)}");
        }

        RemoveTemporary(directory);
    }

    /// <summary>One round of the writing loop: both fields carry the same round number.</summary>
    private static AppState State(int round) =>
        new()
        {
            Settings = new AppSettings { Theme = $"round-{round}" },
            Widgets =
            [
                new WidgetPlacement
                {
                    InstanceId = "stress",
                    WidgetId = "clock",
                    X = round,
                    Y = round,
                    Width = 260,
                    Height = 130,
                },
            ],
        };

    /// <summary>Reads the widget file the import had to leave alone.</summary>
    private static string OldLauncherFile(string widgetsDirectory) =>
        File.ReadAllText(Path.Combine(widgetsDirectory, "launcher.json"));

    /// <summary>The result of SQLite's own integrity check.</summary>
    private static string? IntegrityCheck(string databasePath) =>
        Query(databasePath, "PRAGMA integrity_check;") as string;

    /// <summary>Size and last write time, for telling "left alone" from "written to".</summary>
    private static string Fingerprint(string path) =>
        File.Exists(path)
            ? $"{new FileInfo(path).Length} bytes at {File.GetLastWriteTimeUtc(path):O}"
            : "absent";

    /// <summary>
    /// Waits until the writing process says it has committed something.
    /// </summary>
    /// <remarks>
    /// Polling a file is enough here and needs nothing of the writer beyond the fact
    /// that it records its own progress, which is what makes a failure diagnosable
    /// instead of merely a failure.
    /// </remarks>
    private static async Task<bool> WaitForWriter(Process writer, string markerPath)
    {
        const int step = 100;
        const int limit = 60_000;

        for (var waited = 0; waited < limit; waited += step)
        {
            if (File.Exists(markerPath))
                return true;

            if (writer.HasExited)
                break;

            await Delay(step);
        }

        return File.Exists(markerPath);
    }

    /// <summary>How far the writer got, for the log when it did not get far enough.</summary>
    private static string WriterReport(string databasePath)
    {
        var report = new List<string>();

        foreach (var marker in new[] { ".entered", ".loaded", ".saved", ".failed" })
        {
            if (File.Exists(databasePath + marker))
                report.Add($"{marker}={File.ReadAllText(databasePath + marker)}");
        }

        return report.Count == 0 ? "the writer left no trace" : string.Join("; ", report);
    }

    /// <summary>The source recorded in the database when the old configuration was imported.</summary>
    private static string? ImportedFrom(string databasePath) =>
        Query(databasePath, "SELECT Value FROM Meta WHERE Key = 'imported-from';") as string;

    private Notice? AssertSingleNotice(RecordingNoticePresenter notices)
    {
        if (notices.Notices.Count != 1)
        {
            Fail("exactly one notice is shown", $"{notices.Notices.Count} notices");
            return notices.Notices.FirstOrDefault();
        }

        Check("exactly one notice is shown", true);
        return notices.Notices[0];
    }

    /// <summary>
    /// Verifies that the notice paints a surface of its own, with an edge that can be
    /// told apart from it.
    /// </summary>
    /// <remarks>
    /// Checked on the brushes rather than on pixels on purpose: the failure this
    /// guards against — a surface that paints nothing — is a property of the window,
    /// and reading it here works whatever is on screen, on top, or not being captured.
    /// </remarks>
    private void CheckNoticeSurface(NoticeWindow window)
    {
        // Not "fully opaque": the theme's card is deliberately a little translucent.
        // What matters is that the surface, rather than the wallpaper, decides how the
        // notice looks — a brush that paints nothing leaves the text lying on whatever
        // is behind it.
        const byte Dominant = 0xE0;

        var surface = window.Background;
        var border = (window.Content as Border)?.BorderBrush;

        Note($"surface         : {Describe(surface)}");
        Note($"border          : {Describe(border)}");

        Check("the notice paints a surface of its own",
            surface is ISolidColorBrush { Color.A: >= Dominant },
            Describe(surface));

        Check("the notice has an edge that stands out from its surface",
            border is ISolidColorBrush edge
            && edge.Color.A > 0
            && (surface is not ISolidColorBrush fill || edge.Color != fill.Color),
            Describe(border));

        static string Describe(IBrush? brush) => brush switch
        {
            ISolidColorBrush solid => $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}",
            null => "(none)",
            _ => brush.GetType().Name,
        };
    }

    /// <summary>Records what the shell would have put in front of the user.</summary>
    private sealed class RecordingNoticePresenter : INoticePresenter
    {
        public List<Notice> Notices { get; } = [];

        public void Show(Notice notice) => Notices.Add(notice);
    }
}
