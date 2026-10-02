# DeskKit

A desktop widget platform for Windows, built on Avalonia.

Widgets sit **on the desktop**: above the wallpaper, below every ordinary
window, and they stay put when you press <kbd>Win+D</kbd> or click the taskbar's
"Show desktop" button. Widget windows use a **system surface material** where the
platform has one — Mica on Windows 11 — so the desktop reads through them.

Three widgets ship in the box:

| Widget | What it does |
| --- | --- |
| **Clock** | Time and date, 12/24-hour, optional seconds and weekday |
| **Sticky note** | A note you can type into straight on the desktop |
| **Quick launch** | A grid of shortcuts, fed by dragging files out of Explorer |

The interface is available in **English and Simplified Chinese**, and follows the
system language until you pick one.

## Widget extension model

The runtime depends only on `IWidgetProvider`. Providers are registered with
Microsoft.Extensions.DependencyInjection, and `WidgetRegistry` turns that ordered
set into the catalogue shown by the UI. Built-in widgets use
`services.AddBuiltInWidgets()`; an extension can register its own
`IWidgetProvider` in the same composition root. A duplicate widget ID now fails
at startup instead of silently choosing one provider, which makes plugin
configuration errors actionable.

Widgets communicate through typed, in-process messages exposed by
`IWidgetHost.Messages`; message contracts contain data only, and subscriptions
are disposed with the widget view model. See [the architecture guide](docs/architecture.md)
for module ownership, platform capability boundaries, persistence rules and the
theme/UI model.

This is intentionally **composition-time extensibility**, not yet arbitrary DLL
loading. Loading assemblies from a plugin directory needs an explicit isolation,
dependency-resolution, versioning, and trust policy; adding that before defining
those policies would make the app less predictable, so no loader interface was
added either — an interface with no implementation and no caller is a guess rather
than a seam. [The architecture guide](docs/architecture.md#extensibility-what-is-stable-and-what-is-deliberately-not-built)
lists what is stable today and what a loader would have to decide. When that host
is introduced, [`McMaster.NETCore.Plugins`](https://github.com/natemcmaster/DotNetCorePlugins)
is a good candidate to replace custom `AssemblyLoadContext` and dependency-probing
code. It should be isolated behind one discovery service in the application's
composition root, leaving the widget runtime and registry unchanged.

## Status

The desktop-layer mechanism, the runtime and all three widgets are implemented and
verified on Windows 11. macOS and Linux are not implemented yet; the platform
layer is behind interfaces so they can be added without touching the runtime.

## Requirements

- Windows 10 or 11 (x64 or ARM64); the Mica surface needs Windows 11, and the
  widgets fall back to a plain card without it
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

The unit tests cover the parts that are pure logic: database round-trips, the
schema-migration and refusal rules, the import of the old JSON layout, placement
clamping and snapping, widget settings and their migration, the language
preference, the first-run seed policy, which widget resource keys exist in which
language file, and every branch of the desktop-layer and window-material decision
rules.

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
the real shell against a throwaway database and a throwaway copy of the old JSON
layout to check first-run seeding, pinning, the import, persistence and restore.
The exit code is 0 when every check passes.

One section is different: to measure the durability claim rather than quote it,
the self-test starts a *second copy of itself* (`--stress-write <database>`,
which writes in a loop and refuses any path outside `%TEMP%`), waits until that
copy has committed something, kills it mid-loop, and then opens the database to
check that it opens, that `PRAGMA integrity_check` says `ok`, and that the last
save is whole rather than applied in part. That switch is the only extra way in
to the executable, and it is exclusive on purpose: it either writes to the
temporary path it was given or exits non-zero, and never falls through to
starting the application.

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

### The window surface

A widget has no window chrome, so the surface behind its content *is* the window.
Where the platform can supply one, that surface is a **system material** rather
than a flat colour the app has to repaint whenever the wallpaper changes: Mica on
Windows 11, and the seam for a macOS equivalent, with Liquid Glass declared but
not implemented yet.

The choice travels through `IWindowMaterialService`, and the rules that decide
what a platform can actually honour live in `MaterialPolicy` — pure, so they are
unit-tested instead of discovered on one particular machine. An unsupported
request falls back rather than being reported as granted, because a caller that
believes it has a material lays the window out for one that will never appear:
Mica needs Windows 11, and Acrylic as a distinct choice needs 22H2.

**A material changes the layout, not just the colour.** It is painted by the
window across the window's whole rectangle, so a card inset from that rectangle
would sit on a visible plate of material — the widget would look like a card on a
tray. With a material the card therefore fills the window (`CardMargin` 0) and
hands the rounded corners and the drop shadow to the platform, since neither the
inset nor the card's own shadow has anything left to do. Without one, the inset
and the card-drawn shadow come back.

The card also stops painting its own surface over a material
(`ThemeService.CardBrushFor` returns nothing). How much of the material survives is
exactly `1 - alpha`, so **any** tint is a direct subtraction from the surface the
material exists to provide. That is not a hypothetical: the first attempt painted
the card at 65%, which keeps only 34% of the material, and the result was a
material that was demonstrably on and still looked like a flat colour. The default
background is now the material alone, and `--selftest` asserts the card's alpha is
zero so it cannot drift back.

Because the material *is* the background, the widget content sits directly on it.
The content colours were fixed light-on-dark, which stops working the moment the
surface follows the theme — a light theme gives a light material, so light content
becomes light-on-light. They now come from `WidgetTheme.axaml`, which carries one
set per theme variant, and `--selftest` checks both that the keys resolve and that
they are the right way round (light content in the dark theme, dark in the light
one). That also fixes a latent bug in the no-material path, which was already
handing widgets a near-white card in a light theme.

What a material can and cannot do is worth being clear about. Mica samples the
**wallpaper**, and a widget on the desktop layer has nothing but wallpaper behind
it, so the surface can only ever be as interesting as the wallpaper is: over a flat
colour it will read as a flat colour no matter how transparent the card is. Acrylic
samples whatever is behind the window, which for a bottom-most widget is the same
wallpaper, so it is not a way around that either — it is a one-line change
(`WindowsWindowMaterialService.Default`) and it is a stronger effect, but not a
different one.

Two things about this are worth recording, because both are counter-intuitive:

- **A transparent window can still show Mica.** Widget windows are rendered
  through `WS_EX_NOREDIRECTIONBITMAP` and DirectComposition, and the obvious
  assumption is that this leaves no surface for the compositor to draw a backdrop
  into. Measured on build 26200 it does not: a borderless, contentless window with
  that style set shows Mica, and an otherwise identical transparent window shows
  nothing at all. The material is therefore requested through Avalonia's
  transparency hint, which is what makes the framework create the window in a way
  the compositor can draw behind.
- **Mica Alt currently renders the same as Mica.** It is a real backdrop type and
  the request is honoured, but on a borderless window that carries no frame the
  two are pixel-for-pixel identical. The option exists because the request is
  genuine, not because it looks different.

`--selftest` covers this: it reports the material the machine resolves to, and
checks that the card fills the window, that the corners and the shadow moved to
the platform, that the default background is the material and nothing else, that
the widget content colours are the right way round for the theme, and that a
backdrop was really asked for and is live on the window.

### Languages

The interface ships in English and Simplified Chinese, and localisation is done
with [`Irihi.Lingua`](https://www.nuget.org/packages/Irihi.Lingua): the strings
live in JSON, and a source generator turns every key into a strongly-typed
observable property. Switching culture pushes new values to everything that is
subscribed, so no view has to be rebuilt or reloaded.

```
src/DeskKit.App/Resources/Strings.json             invariant (English)
src/DeskKit.App/Resources/Strings.zh-Hans.json     Simplified Chinese
src/DeskKit.Widgets/Resources/Strings.json         the same, for the widgets
src/DeskKit.Widgets/Resources/Strings.zh-Hans.json
```

**There are two managers, not one.** The application cannot see the widgets' strings
and the widgets cannot see the application's, so each layer declares its own
`[LinguaManager]` class — `AppLanguage` and `WidgetLanguage` — with its own
resource files. This is the library's decentralised model rather than a
workaround, and it keeps the layering intact: `DeskKit.Core` has no dependency on
the localization library, because the only thing a widget descriptor carries is a
resource **key**, which is not a UI concern. `WidgetText` in the widgets assembly
maps those keys to their observables, so the keys are constants and a typo is a
compile error rather than a blank label. The runtime owns neither manager: it
applies the language it is handed (`ShellEnvironment.ApplyLanguage`), and the
product resolves every string a user reads.

Both managers are driven from one place, `LanguageService` in the application.
Driving them separately is what would produce the failure this prevents: a tray
menu in one language and widget names in another. Two details are worth recording:

- **"Follow system" cannot read `CultureInfo.CurrentUICulture` on demand.** The
  service overwrites that culture when it applies a preference, so after switching
  to Chinese, "follow system" would resolve back to Chinese — the setting would
  pass through the value it was supposed to replace. The machine's own language is
  therefore captured once, from `CultureInfo.InstalledUICulture`.
- A stored preference is **not** validated against a list of known cultures. It is
  passed to the resource lookup as given and falls back to the invariant file, so
  adding a translation later needs no code change and a config copied from another
  machine cannot break startup.

What is translated, and how:

| Where | How |
| --- | --- |
| XAML (settings window, widget panels) | `{Translate {x:Static ...+Keys.Key}}` |
| Drop-down choices | The stored **key** is a separate field from the label. The theme picker used to match a selection back from its own display text, which cannot survive the labels being translated — the saved value would depend on the language that was active when it was picked. |
| Tray and context menus | Subscribed to the observable, so the headers change in place instead of leaving the menu in the language it started in. |
| Widget names and descriptions | Descriptors carry resource keys; the product (`WidgetCatalog`) resolves them when it builds a list, and re-reads that list when the culture changes. |
| File dialog title and filter | Read at the moment the dialog opens, so it is in the language active then. |
| Sticky note paper names | The note subscribes to the culture change, because its palette is the one piece of widget text that is not in XAML. |

`--selftest` covers this: it checks that every registered widget names a key this
build owns, that switching language changes what the strings say, that the two
languages come from the two different resource files, and that an unknown language
falls back to English rather than blanking the UI.

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

Snapping is measured between **cards, not windows**. The window normally carries a
transparent margin so the card's drop shadow has room, and measuring that instead
would leave two snapped widgets 32px apart on screen while the code believed the
gap was 8. With a surface material that margin is zero, and the two coincide.

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
two ends, plus a brighter band on the outermost pixels. A border carries one
brush, and one brush cannot fade along two axes.

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

On top of that falloff, the outermost pixels of the edge carry a **specular
band** (`EdgeHighlightWidth` 1.5 DIP, `EdgeHighlightOpacity` 0.75, colour
`HighlightColor`) standing in for the reflection a lit edge shows. Without it the
brightest point on the card is still a tint of the accent colour, which reads as a
tinted panel rather than as light arriving from somewhere; the band makes the edge
itself the brightest thing. It is painted after the glow, so it lifts those pixels
rather than replacing them, it shares the along-edge mask so its ends soften with
the light it sits on, and it is kept to a couple of DIPs because a reflection is a
line, not a region. Measured across the edge it reads roughly 95 → 205 luma at the
outermost pixel and is gone again by 1.5 DIP.

The band also has to **turn the corners**. A band that hugs a line at a fixed
distance from the edge falls outside the card as soon as the outline starts to
curve — on a 14 DIP radius it is outside for the first 8 DIP of the turn and gets
clipped away, so the reflection visibly stops short of the corner. So the straight
part is cut back to the tangent point and a corner pass continues from there,
rebuilding the band as arcs of the card's own corner circles. A gradient cannot
bend, so a corner is drawn as `CornerBands` concentric sub-bands, each taking the
intensity the gradient would have had partway across it; four of them over 1.5 DIP
puts every step well under a pixel. A corner reached from both edges at once — a
diagonal placement — is drawn once, not twice, or it would come out brighter than
the edge leading into it.

The card is inset inside its window by `WidgetWindow.GlowMargin` — or not inset at
all when the window carries a surface material, in which case the card and the
window are the same rectangle. Without a material, anything a window paints
outside itself is clipped, so the window has to be larger than the card for the
card's own drop shadow to be visible at all. Stored placements describe the
visible card, so the margin never leaks into the saved layout either way.

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
  DeskKit.Core/         widget contract, state records, pure rules, port contracts
  DeskKit.Runtime/      the widget runtime: state, lifetime, windows, placement, appearance
  DeskKit.Persistence/  the database: EF Core, migrations, backup, old-JSON import
  DeskKit.Platform/     OS interop: window pinning, surface materials, shell icons, autostart
  DeskKit.Widgets/      the built-in widgets and their strings
  DeskKit.App/          the product: composition root, tray, settings window, notices, self test
tests/
  DeskKit.Core.Tests/
  DeskKit.Runtime.Tests/
  DeskKit.Persistence.Tests/
  DeskKit.Platform.Tests/
  DeskKit.App.Tests/
scripts/
  New-AppIcon.ps1      regenerates Assets/deskkit.ico from scratch
```

Dependencies point one way: `Core` references nothing else, `Runtime`,
`Persistence`, `Platform` and `Widgets` reference only `Core`, and `App`
references all five. `DeskKit.Runtime.csproj` fails the build if it is given any
project reference but `DeskKit.Core`, so the runtime cannot quietly grow a
dependency on the database, the Win32 implementation or the built-in widgets.

## Data

Nothing is sent to a server. Everything the application persists lives in one SQLite
database, `%LOCALAPPDATA%\DeskKit\deskkit.db`, and logs go to
`%LOCALAPPDATA%\DeskKit\logs\`.

```
%LOCALAPPDATA%\DeskKit\
  deskkit.db                        preferences and every placed widget
  deskkit.db-wal, deskkit.db-shm    write-ahead logging, while the app is running
  deskkit.pre-migration-backup.db   a whole copy, taken before a schema migration runs
  logs\

%APPDATA%\DeskKit\                  the JSON this used to be: read once, then left alone
  settings.json
  widgets\
  config.json
```

**Why a database, and what it cost.** The configuration used to be JSON: a
`settings.json` with the preferences, and one file per widget. That layout was readable
and patchable by hand, and a damaged file cost exactly one widget — at the price of a
hand-written atomic write, a version number per file, a rule for every way a file could
go wrong, and a migration pipeline with no tooling behind it. Those are now SQLite's
commit protocol and EF Core's migrations. The trade is real, so it is stated rather
than implied:

| Property | As JSON files | As a database |
|---|---|---|
| Editable by hand | yes, in any text editor | only with a SQLite client |
| Blast radius of damage | one widget | the whole layout |
| User content travels with a roaming profile | yes | **no** |
| Interrupted write | hand-written temporary file, flush, swap | SQLite's commit protocol |
| Schema version and migrations | hand-written | EF Core migrations |

The database is in the **local** folder and cannot go back to the roaming one: it is a
single file written in place, so a profile copy or a folder redirection that reaches it
while it is open is a way to corrupt it. The consequence is plain, and worth saying
rather than burying: the text of a note and the shortcuts in a launcher no longer
follow a roaming profile to another machine.

**What a build does with what it finds.** The schema is the set of EF Core migrations
compiled into the build, and the database records which of them it has applied in
`__EFMigrationsHistory`. Each situation is decided on its own:

| On disk | What happens |
|---|---|
| nothing, and no old JSON | first run: the database is created, and one clock is seeded |
| the old JSON, no database | imported once; from then on only the database is written |
| a database this build understands | read, migrated if migrations are pending, written as usual |
| a database with a migration this build has never heard of | **nothing is read and nothing is written.** A notice says so, and the application exits rather than showing an empty desktop it cannot save |
| a database that cannot be opened, or is not a database | nothing is read and nothing is written; the session runs with nothing loaded, refuses every save, and says so |
| a widget's stored settings that cannot be parsed | that widget comes up with its defaults; the rest of the layout is unaffected |

Before a migration runs, a whole copy of the database is taken to
`deskkit.pre-migration-backup.db` — unless there is nothing to lose yet, in which case
copying it would only overwrite a good backup with an empty one. The copy goes through
SQLite's own backup API rather than a file copy, because in write-ahead logging the
newest commits are still in the `-wal` file: copying `deskkit.db` on its own can
produce a backup that is missing them. A test asserts exactly that.

The notice is drawn by the application rather than raised as a tray balloon: Avalonia's
`TrayIcon` has no balloon of its own — it holds the platform's notification data
internally and does not expose the handle an extra one would need, so registering a
second would mean a second tray icon. It appears once, in the corner a balloon would
have used, does not take focus, and closes on a click or after a few seconds.

It asks to be topmost, but whether it actually ends up in front of a maximised window
could not be confirmed here: on the machine this was built on, no window could be made
topmost at all — an unrelated WinForms window with `TopMost = true` came out without it
too, so that measurement says nothing about this window in particular. The same warning
is also put on the tray icon's tooltip, which is worth having anyway: it does not
expire when the notice does, and it cannot be covered by anything.

Two further rules protect the layout itself, both of which exist because breaking
them destroys it silently:

- **An adjustment made for the current displays is not saved as if the user had
  made it.** A widget whose saved position is on a monitor that is not connected is
  shown on the nearest one, but the saved position is left alone, so plugging that
  monitor back in puts the widget where it was. Folding the adjustment into the
  saved layout is what used to make a layout drift a little on every undock — and
  on a roaming profile it made two machines overwrite each other's positions.
  Positions are only written when the user actually drags or resizes a widget.
- **One instance per logon session.** A named mutex keeps a second launch from
  starting a rival set of widgets and overwriting the first one's files; the
  second instance logs why and exits. The mutex is a kernel object, so it is
  released even on a crash and there is no stale lock to clean up.

One thing is reconciled rather than trusted: the **start-with-Windows** preference
is registered per user *per machine*, but it travels with the profile, so a config
that says "on" is routinely wrong on a second computer. The registry is treated as
the truth and the stored value is corrected at startup — the alternative, writing a
run key because a file said so, is a side effect nobody asked for on that machine.

**Two sessions of the same user** (console plus RDP, or fast user switching) share the
one database. SQLite takes a lock for the length of a transaction and waits out a short
one, so two sessions can no longer corrupt each other's state the way two writers of
the same file could; the later write still wins, which is the honest limit of that
guarantee. The single-instance mutex stays session-scoped on purpose: making it
machine-wide would leave a session the user is actually sitting in with no widgets.

**Durability.** The database runs in write-ahead logging and flushes every commit to
the disk before reporting it done, so a commit that returned survives a crash or a
power loss, and an interrupted write cannot leave a half-written layout behind — SQLite
rolls the transaction back.

**Coming from the JSON layout.** The old files are read once, into a database that does
not exist yet, and are then left exactly as they are: not deleted, not renamed. Which
of them is read is decided in this order:

1. `settings.json` and `widgets\*.json`, if either is present,
2. otherwise `config.json`, the single file everything used to be in,
3. otherwise `config.pre-split-backup.json`, where a build that split that file had
   moved it.

A widget file that cannot be parsed is skipped and the rest are still imported: one
damaged file used to cost one widget, and it must not now cost the layout. The import is
idempotent — it runs only while the database holds neither preferences nor widgets — so
a first run that was interrupted is finished on the next one.

Because the old files are never removed, a build from before this change still finds its
layout where it left it. What it will not see is anything done afterwards: from here on
the database is the only thing written.

### Widget settings

A widget's own configuration is an opaque dictionary to the shell, which is what lets
a widget change what it persists without the shell changing at all. The cost lands on
the day the shape changes, so each widget declares the version of its own settings and
the steps that bring older ones forward:

```csharp
public sealed class QuickLaunchWidgetProvider : IWidgetProvider, IWidgetSettingsMigrations
{
    public int SettingsVersion => 2;

    public IReadOnlyList<WidgetSettingsMigration> Migrations { get; } =
    [
        new(1, settings => /* the shortcut list, rewritten in the file's own naming */),
    ];
}
```

The shell keeps that number in the widget's file and runs the steps in order without
looking inside them. Three rules make that safe: the whole chain is checked before any
step runs, because a step that has already rewritten the settings cannot be undone; a
missing step leaves the version alone, so the next start tries again rather than
believing a migration that never happened; and settings written by a newer build are
left exactly as they are.

Nested objects inside a widget's settings are written with the same naming as the rest
of the file, and read back case-insensitively, so a file written before that was
settled still loads. `WidgetSettings.Get`/`Set` are reflection based — they are open
generics, so no source-generated contract can be closed over them — which is why
trimming and AOT stay off until a widget can hand its settings type over as a
`JsonTypeInfo`.

Positions are stored in **physical** pixels and sizes in **logical** pixels,
because that is what the window manager and the layout system each report.
Mixing them silently is the classic way to get widgets that drift on high-DPI
displays.

## Adding a widget

1. Implement `IWidgetProvider` (descriptor plus a factory) and a
   `WidgetViewModel` with `CreateView`.
2. Register it in `BuiltInWidgets.CreateProviders`.
3. Add its name and description to both `Resources/Strings.json` files under
   `Widget`, add the matching constants and mappings to `WidgetText`, and point the
   descriptor at them. `WidgetResourceKeyTests` checks that both language files
   define every key the built-in descriptors name, so a key that only reaches one
   file fails the test run rather than showing up as raw text in that language.
4. When the shape of the widget's own settings changes later, implement
   `IWidgetSettingsMigrations` on the provider and add a step — see
   [Widget settings](#widget-settings).

A new widget is **not** put on the desktop by a first run unless it is added to
`DefaultLayout` in the application. Which widgets a new user gets is a product
decision, and the runtime never looks at a descriptor for it: it is handed the
list of ids to create and knows nothing else about them.

The runtime knows a widget only by the id in its descriptor and never resolves
user-visible text: names are resolved by the product (`WidgetCatalog`), and the
language is handed to the runtime as a single delegate
(`ShellEnvironment.ApplyLanguage`). A future plugin loader can therefore add
providers from separate assemblies without changing anything that runs a widget.

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
