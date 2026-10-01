# DeskKit

A desktop widget platform for Windows, built on Avalonia.

Widgets sit **on the desktop**: above the wallpaper, below every ordinary
window, and they stay put when you press <kbd>Win+D</kbd> or click the taskbar's
"Show desktop" button.

Three widgets ship in the box:

| Widget | What it does |
| --- | --- |
| **时钟 (Clock)** | Time and date, 12/24-hour, optional seconds and weekday |
| **便签 (Sticky note)** | A note you can type into straight on the desktop |
| **快捷启动器 (Quick launch)** | A grid of shortcuts, fed by dragging files out of Explorer |

## Status

The desktop-layer mechanism, the shell and all three widgets are implemented and
verified on Windows 11. macOS and Linux are not implemented yet; the platform
layer is behind an interface so they can be added without touching the shell.

## Requirements

- Windows 10 or 11 (x64 or ARM64)
- .NET 10 SDK to build

## Build and run

```powershell
dotnet build DeskKit.slnx
dotnet run --project src/DeskKit.App
```

`DeskKit.slnx` is the newer XML solution format used by the .NET 10 SDK.

## Tests

```powershell
dotnet test DeskKit.slnx
```

The unit tests cover the parts that are pure logic: configuration round-trips,
corrupt-file recovery, placement clamping, widget settings, and every branch of
the desktop-layer decision rules.

### Verifying the desktop behaviour

Pinning a window to the desktop, surviving "Show desktop", and rendering with
per-pixel transparency cannot be proven by a unit test, and a driver-dependent
failure (a widget that paints as a solid black rectangle) is invisible to
someone reading logs. So the application ships a self-test that exercises a real
window on a real desktop:

```powershell
dotnet run --project src/DeskKit.App -- --selftest --out artifacts/selftest-report.txt
```

It shows a probe widget, drives every code path the shell uses to hide a window
(<kbd>Win+D</kbd>, `SC_DESKTOP`, `ShowWindow(SW_SHOWMINIMIZED)`, a
`SWP_HIDEWINDOW` request, a raise-to-top request), photographs the result with
`BitBlt` to confirm the rounded corner really is see-through, and finally drives
the real shell against a throwaway config directory to check first-run seeding,
pinning, persistence and restore. The exit code is 0 when every check passes.

## How the desktop layer works

The obvious approach — reparenting the window into Explorer's wallpaper
`WorkerW`, the way Rainmeter does — **does not work here**. A reparented window
becomes a child window, DWM stops composing it like a top-level window, and
Avalonia renders transparency through `WS_EX_NOREDIRECTIONBITMAP` and
DirectComposition. On many GPU/driver combinations the widget then paints as a
solid black rectangle: alive, hit-testable, and invisible. Driver-dependent
failure is the worst kind, so DeskKit does not do it.

Instead the window stays an ordinary top-level window — hardware rendering, DWM
corners and shadow intact — and is kept at the very bottom of the z-order by a
`WndProc` hook that intercepts every path which could hide, minimise or raise it:

| Message | Why |
| --- | --- |
| `WM_SYSCOMMAND` `SC_MINIMIZE` / `SC_MAXIMIZE` / `SC_DESKTOP` | <kbd>Win+D</kbd>, <kbd>Win+Down</kbd>, the taskbar button, and the Windows 11 four-finger swipe |
| `WM_SIZE` `SIZE_MINIMIZED` | the direct `ShowWindow(SW_SHOWMINIMIZED)` path |
| `WM_WINDOWPOSCHANGING` | forces `HWND_BOTTOM`, turns a hide request into a show, and rejects a collapse to the caption icon rect |
| `WM_ACTIVATEAPP`, `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, shell hook | cheap re-pins that catch anything the paths above miss |

The rules live in `DesktopLayerPolicy`, a dependency-free static class, so they
are unit-tested rather than discovered by watching the screen.

Two deliberate consequences:

- Widgets exist on one virtual desktop at a time.
- An intentional hide (the tray's "hide all widgets") suspends the hook, which
  is what `IDesktopLayerService.SetVisible` does.

## Project layout

```
src/
  DeskKit.Core/        models, configuration, widget contract (no platform code)
  DeskKit.Platform/    OS interop: window pinning, shell icons, autostart
  DeskKit.Widgets/     the built-in widgets
  DeskKit.App/         Avalonia shell: window host, tray, settings window
tests/
  DeskKit.Core.Tests/
  DeskKit.Platform.Tests/
scripts/
  New-AppIcon.ps1      regenerates Assets/deskkit.ico from scratch
```

## Data

Everything is local. Configuration lives in
`%APPDATA%\DeskKit\config.json`, logs in `%APPDATA%\DeskKit\logs\`.

The config file is written atomically (temporary file, then swap), so an
interrupted write cannot leave a half-written layout behind. A file that cannot
be parsed is moved aside as `config.corrupt-<timestamp>.json` rather than
deleted.

Positions are stored in **physical** pixels and sizes in **logical** pixels,
because that is what the window manager and the layout system each report.
Mixing them silently is the classic way to get widgets that drift on high-DPI
displays.

## Adding a widget

1. Implement `IWidgetProvider` (descriptor plus a factory) and a
   `WidgetViewModel` with `CreateView`.
2. Register it in `BuiltInWidgets.CreateProviders`.

The shell only knows about `IWidgetProvider`, so a future plugin loader can add
providers from separate assemblies without changing the shell.

Set `PreventActivation: true` in the descriptor for any widget that does not
need the keyboard: such widgets are given `WS_EX_NOACTIVATE` so clicking them
does not pull focus away from the application the user is working in. Only the
sticky note leaves it false.

## Icon asset

`src/DeskKit.App/Assets/deskkit.ico` is generated, not hand-drawn:

```powershell
pwsh -File scripts/New-AppIcon.ps1
```

## License

Not chosen yet.
