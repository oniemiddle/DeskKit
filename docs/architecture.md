# DeskKit architecture

## Design boundary

DeskKit is a desktop host, not a collection of special-case windows. The host
owns process lifetime, persistence, platform integration and desktop placement.
Widgets own their own view, settings schema and widget-specific behaviour. The
boundary between the two is `DeskKit.Core`'s provider and host contracts.

```
src/
  DeskKit.Core/         widget contract, domain records, pure rules, port contracts
  DeskKit.Runtime/      the widget runtime: state, lifetimes, windows, placement, appearance
  DeskKit.Persistence/  the database: EF Core, migrations, backup, old-JSON import
  DeskKit.Platform/     OS interop: window pinning, surface material, shell icons, autostart
  DeskKit.Widgets/      the built-in widgets and their strings
  DeskKit.App/          the product: composition root, tray, settings, notices, self test
```

Dependencies always point down this diagram, and the build enforces the sharpest
edge of it (`DeskKit.Runtime.csproj` fails if it is given any reference but
`DeskKit.Core`):

```
        Core
       ↑  ↑  ↑  ↑
Runtime  Persistence  Platform  Widgets
       ↑  ↑  ↑  ↑
        App
```

`Core` holds the widget contract, the state records, the pure rules that decide
placement and settings versions, and the ports (`IStateStore`,
`IDesktopLayerService`, `IWindowMaterialService`, `INoticePresenter`,
`IWidgetMessageBus`, `IShellIconLoader`). It references no other DeskKit project
and no database stack.

`Runtime` runs widgets: it owns the in-memory state, the widget instances and
their lifetimes, the windows they are drawn in, the snapping rules and what the
theme does to a live surface. It knows the widget contract and the ports it
consumes, and nothing else — no tray, no settings window, no database, no Win32.

`Persistence` is the only project holding EF Core and SQLite, and the only place
`WidgetPlacement` is mapped to a database entity.

`App` is the product. It is the composition root, it owns everything the user
sees around the widgets (tray icon, settings window, context menu, notices),
and it is the only project that references all the others.

A widget cannot call Win32, cannot reach into another widget's view model, and
cannot be named by the runtime: the runtime never resolves user-visible text.

## Platform capabilities

`DeskKit.Platform` expresses host integration as capabilities, and which
implementation a capability gets is decided when the assembly is compiled, not when
it runs. The project targets two frameworks:

* `net10.0-windows` defines `WINDOWS`. The `Windows` folder and the interop it calls
  are compiled only into this leg, and `AddDeskKitPlatform()` registers them.
* `net10.0` compiles those files out, and `AddDeskKitPlatform()` registers the no-op
  capabilities instead.

The product targets `net10.0-windows` and therefore ships the Windows leg; the
neutral leg is what keeps the no-op path compiling instead of letting it rot. No
file under `Windows/` tests the operating system, because none of it is compiled
where the answer could be no. The contracts the runtime consumes live in
`DeskKit.Core`, because the runtime may only reference Core (D-3/D-6); the
implementations, and the capabilities only the product uses, live here:

| Capability | Contract | Windows implementation | Neutral implementation |
| --- | --- | --- | --- |
| Desktop placement and deliberate visibility | `Core`: `IDesktopLayerService` | `WindowsDesktopLayerService` | `NullDesktopLayerService` |
| Desktop's own double-click | `Core`: `IDesktopGestureService` | `WindowsDesktopGestureService` | `NullDesktopGestureService` |
| Window material | `Core`: `IWindowMaterialService` (+ `WidgetMaterial`) | `WindowsWindowMaterialService` | `NullWindowMaterialService` |
| File icons | `Core`: `IShellIconLoader` | `WindowsShellIconLoader` | `NullShellIconLoader` |
| Login startup | `Platform`: `IAutoStartService` | `WindowsAutoStartService` | `NullAutoStartService` |
| Notification window styling | `Platform`: `INotificationWindowStyler` | `WindowsNotificationWindowStyler` | `NullNotificationWindowStyler` |

The rules that decide *whether* a capability can be honoured stay here as
dependency-free static classes (`MaterialPolicy.Resolve`, `DesktopLayerPolicy`,
`DesktopBackdropPolicy`, `DesktopDoubleClickDetector`), so they are unit-tested
rather than discovered on one particular machine. Note that `MaterialPolicy` still
takes the build number: "Mica is available" is a question about the machine,
whereas "this is a Windows build" is a question about the compilation, and only
the second one becomes a target framework.

`AddDeskKitPlatform()` is the only platform selection point used by the app, and it
holds no runtime test — the compiler has already answered. Adding an OS therefore
means adding a target framework and the implementations for it, not adding
`OperatingSystem.Is…` branches to views or widgets. A null capability must be a safe
fallback, never an exception for a feature the host can simply omit. The one
production file that used to name the Win32 implementation directly — the notice
window — now asks its `INotificationWindowStyler` port whether styling is supported
rather than asking the OS. The desktop self test still checks
`OperatingSystem.IsWindows()` on purpose, and it is the only place left that does:
it reports which machine it is running on and skips raw interop rather than letting
it fail, which is a question about the host, not a choice of implementation.

## Widget SDK and lifecycle

The SDK surface for a widget is deliberately small:

* `IWidgetProvider` describes a stable widget type and creates a view model for
  one placement.
* `WidgetDescriptor` contains only identity, size and interaction policy; UI text
  is a resource key so it can follow a runtime culture change.
* `WidgetContext` supplies a creation-time placement snapshot, versioned settings
  and `IWidgetHost`. The placement does not follow the window afterwards: a
  widget that needs where it is now asks the window it is drawn in.
* `WidgetViewModel` owns its Avalonia controls and is started after its window is
  visible, then stopped and disposed before the window closes.

Providers are contributed through DI. `WidgetRegistry` preserves registration
order and rejects empty or duplicate IDs. This makes built-ins, test widgets and
future plugins use one composition seam. Runtime DLL discovery is intentionally
not part of this boundary until plugin versioning, isolation and trust policies
are specified.

What the runtime offers the product besides the widget contract are two seams:
`IShellFacade.WidgetAdded`, raised for each widget as it appears so the product
can hang its own chrome (the context menu) on the window, and
`IShellFacade.SettingsRequested`, raised when a widget asks for its own settings.
The runtime builds no menu and owns no settings window.

## Widget communication

Widgets must not retain each other's view models. `IWidgetMessageBus` provides
typed, in-process publish/subscribe contracts through `IWidgetHost.Messages`.
Message types should be immutable data records in a small shared contract
assembly. Delivery is synchronous on the publisher's thread; UI subscribers must
marshal to Avalonia's UI thread where necessary, and each widget must dispose its
subscriptions with its view model. Persistent or cross-process workflows do not
belong on this bus.

For request/response, start with an explicit service contract owned by the host.
Do not layer a generic request bus over widget messages: doing so obscures
ownership, cancellation and failure handling.

## State and settings

`StateStore` is the single writer on disk for `AppState`, and SQLite is the source
of truth. It owns EF migrations, the pre-migration backup, old JSON import and the
read-only refusal rules for unavailable or newer schemas. The in-memory copy is
owned by the runtime's `WorkspaceState`, which debounces ordinary writes and
raises the change the open UI reacts to; nothing else asks the store to write.
Widget settings are stored as versioned JSON per placement and migrated by the
widget provider. `WorkspaceState` debounces ordinary writes, but migrations and
first-run seeding are saved immediately, and first-run seeding itself belongs to
the product: the runtime is handed the widget ids a first run should create and
never reads a default layout of its own.

New host-level data belongs in a typed `AppState`/EF entity migration. New
widget-level data belongs in the widget's settings plus an
`IWidgetSettingsMigrations` step. Neither should create an independent file.

## UI, theme and desktop behaviour

`ThemeService` owns application theme selection. Widgets consume semantic keys
from `WidgetTheme.axaml`; they must not hard-code light or dark content colours.
`IWindowMaterialService` resolves a requested platform material before widget
window layout is calculated, while `WidgetWindow` owns window-only concerns such
as card margins, drag/resize and snap glow.

`WidgetShell` is a facade: it sequences a session's startup, implements
`IWidgetHost` and `IShellFacade`, and holds the few decisions that need more than
one collaborator. Everything else belongs to a named owner —
`WorkspaceState` (the state), `WidgetRuntimeHost` (widget instances and their
lifetimes), `WidgetSurfaceFactory` (window construction), `PlacementController`
(snapping and write-back), `AppearanceController` (theme and material on a live
surface), `WidgetVisibilityAnimator` (sliding the surfaces when they are shown
and hidden) and `ShellStartup` (what a load report is worth saying and how stored
settings catch up). It builds no UI of its own: the tray icon, the settings
window, the context menu and the storage notice are the product's, wired in the
composition root, which is also where start-with-Windows is reconciled. It is
currently 249 lines, and the reason to keep it that way is that the previous
947-line version was the same facade with all six of those jobs inside it.

## Extensibility: what is stable, and what is deliberately not built

This section is design only. No plugin loader exists in the codebase, and none is
planned until the policies below are decided.

### The seams that are already stable

A third-party widget is written against the same contract a built-in widget is,
and every one of these is unchanged by the refactor:

* `IWidgetProvider` — `Descriptor` and `Create(WidgetContext)`.
* `WidgetDescriptor` — identity, default and minimum size, interaction policy. It
  carries no seeding flag: which widgets a first run gets is the product's.
* `WidgetContext` — the creation-time placement, versioned settings, `IWidgetHost`.
* `WidgetViewModel` — `CreateView`, `CreateSettingsView`, `Start`, `Stop`,
  `Dispose`, and `ITickAware` for periodic work.
* `IWidgetHost` — screens, the message bus, `RequestSave`, and the two requests a
  widget can make of the product (open my settings, remove me).
* `IWidgetMessageBus` and `IWidgetSettingsMigrations` — communication and settings
  history stay owned by the widget.

Registration is the composition seam: providers are added through DI and
`WidgetRegistry` preserves order and rejects duplicate ids. Identity is the id in
the descriptor, and a collision is a startup failure rather than a silent choice
between two providers.

### What the runtime deliberately does not know

The runtime never resolves user-visible text. A descriptor carries a resource key
(not a name), the product resolves keys through `WidgetCatalog`, and the language
reaches the runtime as one delegate (`ShellEnvironment.ApplyLanguage`). Two
consequences are worth stating plainly:

* A widget from outside this build can hand over a key the application cannot
  resolve. The documented behaviour is that the key itself is shown — visible and
  diagnosable, not blank — which is what makes a stale layout entry or a missing
  translation obvious.
* A future loader is a *product* addition: it would discover providers and add
  them to DI, and it would supply its own name resolution. Nothing in
  `DeskKit.Runtime` has to change, which is the point of having removed the two
  dependencies the old shell had on built-in widgets.

### Why no plugin loader is here yet

`IWidgetProviderSource` — an interface whose only implementation would be a loader
— was considered and **not** added. A contract with no implementation and no
caller is not a seam; it is a guess, and it would have to be designed against
policies that do not exist yet. Loading assemblies from a directory requires
deciding, at minimum:

1. **Identity and collisions.** Which component owns an id, and what happens when
   two do. `WidgetRegistry` already fails a duplicate, but a plugin host has to
   decide whether one failing plugin aborts startup or is skipped. The id a
   third-party widget would be allowed to use is part of this and is not specified
   yet: the rule that a widget id carries a vendor or application prefix, so that a
   plug-in can never collide with a built-in, is recorded as a decision to take
   before any loader is written.
2. **Versioning.** What a plugin is compiled against, and what happens when the
   host's contract moves. The widget contract is source-compatible today, not
   binary-stable.
3. **Isolation and unload.** Whether plugins load into the default
   `AssemblyLoadContext` (shared types, no unload) or their own (duplicated
   contracts, unload possible but never when a window outlives it).
4. **Trust.** Where plugins may come from, whether they are signed, and what they
   are allowed to touch — noting that a widget runs in-process and can already
   draw a window.
5. **Failure containment.** What happens when a plugin's `Create` or `Start`
   throws. The runtime already logs and skips a widget that fails to be created,
   and that behaviour is what a loader would build on.

When those are decided, [`McMaster.NETCore.Plugins`](https://github.com/natemcmaster/DotNetCorePlugins)
is a better starting point than hand-written `AssemblyLoadContext` probing, and it
belongs behind one discovery service in the application's composition root.

### The extension points that are kept open on purpose

* Providers are resolved from DI, so a loader can add them the same way
  `AddBuiltInWidgets` does.
* `WidgetRegistry` is the only catalogue the runtime sees, so nothing in the
  runtime needs a compile-time list of widget types.
* `WidgetInfo`/`WidgetRuntime` are what the product's UI lists, so a plugin's
  widgets appear in the settings window and the tray menu without UI changes.
* `ShellEnvironment` is the single injection point for product data, so anything
  else a future host must hand the runtime is added in one place rather than
  threaded through the shell.

## Library decisions

The project already uses Avalonia, CommunityToolkit.Mvvm, EF Core and Microsoft
DI where they reduce framework plumbing. Keep the message bus small and local:
MediatR would add indirection without solving a current cross-process or pipeline
need. If DLL plugins are introduced, evaluate `McMaster.NETCore.Plugins` to avoid
hand-written `AssemblyLoadContext` dependency resolution. For richer settings
validation, evaluate `FluentValidation` only once user-editable settings have
rules that are shared by UI and persistence; annotations or view-only validation
would otherwise split the rule source.
