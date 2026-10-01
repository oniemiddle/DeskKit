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

### Moving, resizing and snapping

#### The drag strip

Every widget has a drag strip across the top of its card. It is part of the
window chrome rather than of any widget, so a widget can rely on it existing —
which matters for the sticky note, whose text boxes fill the rest of the surface
and would otherwise leave nothing to grab. Dragging still also works on any part
of the card that is not itself interactive (a button, a text box).

The strip is an **overlay, not a reserved row**: the widget content keeps the
whole card and the strip floats on top of the first 16px of it. Its background is
transparent for exactly that reason — anything else would paint over the content
underneath. The only thing it draws is a short rounded bar centred in it, which
is pure decoration: absent at rest, faded in while the pointer is over the
widget, and painted in the accent colour while the widget is being moved. The bar
carries a small drop shadow, because a translucent white pill on a light
background would otherwise be invisible. Hovering the strip deliberately does not
change the cursor; the resize bands do show a resize cursor, since an invisible
hotspot with no cursor is undiscoverable.

Because the strip overlays the content, a widget whose first row of content is
interactive keeps a little top padding so its controls do not sit underneath the
strip. Both widgets with top-anchored content (the sticky note's title box and
the launcher's first row of tiles) leave 18px of clearance.

#### Magnetic snapping

Dragging a widget near another one snaps them together. The rules live in
`WidgetSnapEngine` and are pure, so they are unit-tested rather than tuned by
feel:

- **Adjacent**, with an 8px gap. Widgets are meant to read as separate objects,
  so they never end up flush against each other. This only applies when the two
  already overlap on the other axis — a widget far below another will not be
  glued to its side.
- **Aligned** — sharing a left, right, top, bottom or centre line.

The capture range is deliberately small: an edge snaps when it is within 8px —
the gap itself — of a snap position. This is a local gesture, not a gravity well.
Only widgets whose edges are within 16px of each other take part **at all**;
without that limit a widget could be pulled into alignment with another one on
the far side of the desktop purely because they happened to share an edge, which
reads as global grid alignment rather than as two widgets placed together.

Snapping is measured between **cards, not windows**. The window carries a
transparent margin so the card's drop shadow has room, and measuring that instead
would leave two snapped widgets 32px apart on screen while the code believed the
gap was 8.

#### The magnetism glow

The glow shows the region two widgets actually share, rather than lighting a
whole edge. For a snapped pair, the axis they are *separated* on decides which
edge faces the neighbour, and along that edge only the stretch the neighbour
really covers is lit. So:

- A neighbour shorter than the widget lights only the part of the edge that
  overlaps, with the rest left dark.
- Two neighbours on the same side light two separate stretches.
- A diagonal placement is separated on both axes, so two edges light at once.
- When the rectangles share nothing along the facing edge, a short stretch at the
  nearest end is lit instead, so a diagonal placement still reads as attached.

Segments are expressed as fractions of the card, so they stay correct when the
widget is resized. A segment describes **where** the light is, not how far it
reaches: the renderer adds the spread.

The glow is a **surface effect on the card**. It is the same idea as the halo a
card shows in a web UI when the pointer comes near it — a soft light that answers
to something touching the card — except that the thing it answers to is a
neighbouring widget rather than the cursor. Like that halo it is clipped to the
card's own rounded outline and never reaches past it, so the widget itself is what
lights up and nothing spills into the gap between the two cards.

It is drawn by `WidgetGlowLayer`, a hand-drawn decoration layer rather than a
set of borders, because the shape is not expressible with borders: one edge can
be lit in several stretches, several edges can be lit at once, and each stretch
needs a gradient that fades away from the shared edge while also softening at its
two ends. A border carries one brush, and one brush cannot fade along two axes.

The layer lives inside the card, is never hit-testable and takes no layout space.

The shared region is treated as the **position of a light source**, not as the
extent of the glow. Two things follow from that:

- **Along the edge** the light reaches `SpreadAlongEdge` (28 DIP) beyond the
  shared region at each end, fading out over that reach. Without it the glow stops
  dead where the widgets stop overlapping, which reads as a painted rectangle
  rather than as illumination.
- **Across the edge** the light starts at full strength on the shared edge and
  decays *inwards* over `HorizontalFadeLength` / `VerticalFadeLength`. The edge
  itself is where the light is brightest, which is what makes the two cards read
  as joined; the falloff is what keeps it from looking like a painted stripe.

The fade is directional. A vertical stretch fades across the card's width, which
is its long side, so a fade that looks right on a top or bottom edge looks like a
tight stripe on a left or right one: `HorizontalFadeLength` (30 DIP) exceeds
`VerticalFadeLength` (18 DIP).

Because the fade is short, a fully opaque edge colour reads as a painted stripe
rather than as light, so the intensity is shaped too:

- `EdgeOpacity` (0.6) sets the alpha at the shared edge — deliberately well below
  opaque.
- `FalloffExponent` (1.5) bends every ramp — the fade into the card and the
  reach along it. Light decays faster near its source, so a linear ramp looks like
  a wedge; sampling an ease-out curve gives a decay of roughly 153 → 99 → 54 → 19
  → 0 over the fade, instead of 255 → 191 → 128 → 64 → 0.

The card is inset inside its window by `WidgetWindow.GlowMargin`. Anything a
window paints outside itself is clipped, so the window has to be larger than the
card for the card's own drop shadow to be visible at all. Stored placements
describe the visible card, so the margin never leaks into the saved layout.

#### Dragging

Dragging is implemented by hand rather than through `BeginMoveDrag`, because the
system move loop reorders the window and fights the "always at the bottom" rule.

The arithmetic lives in `WidgetDragSession`, and its one rule is that the window
position is a function of **the pointer's current screen position** and a grab
offset captured when the button went down — never of the window's own current
position. Deriving it from the live window position feeds the window's own
movement back into the calculation, and `x ← pointer − x` oscillates: the widget
lurches back towards where the drag started instead of following the cursor, and
tracks at roughly half speed in between. `WidgetDragSessionTests` pins this down,
including an executable record of the broken formula.

Magnetism is applied *after* that calculation, to the already-correct position,
so it nudges the widget without ever feeding back into the pointer tracking.

#### Resizing

The whole card perimeter is a resize handle — left, right and bottom edges, plus
all four corners — with two deliberate details:

- The card's **top band is left to the drag strip**, because the two would
  otherwise overlap and make the strip unreliable. The top *corners* still
  resize, so the top edge is not a dead zone.
- Edge bands are 5px wide, which lands in the widget's padding rather than on its
  controls.

The system resize loop is not used, for the same reason as `BeginMoveDrag`: it
reorders the window and fights the bottom-most pinning rule. The geometry lives in
`WidgetResizeSession` and follows the same discipline as the drag — the result is
a function of the pointer and of the rectangle captured when the button went
down, never of the window's current size. Dragging an edge moves that edge and
leaves the opposite one anchored, and the widget's minimum size is enforced by
pushing back the edge being dragged rather than by moving the anchored one.
`WidgetResizeSessionTests` covers the hit testing and all eight directions.

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
