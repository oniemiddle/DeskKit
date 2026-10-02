# DeskKit architecture

## Design boundary

DeskKit is a desktop host, not a collection of special-case windows. The host
owns process lifetime, persistence, platform integration and desktop placement.
Widgets own their own view, settings schema and widget-specific behaviour. The
boundary between the two is `DeskKit.Core`'s provider and host contracts.

```
DeskKit.App (Avalonia composition, shell, settings/tray)
 ├── DeskKit.Widgets (built-in widget modules)
 ├── DeskKit.Platform (capability implementations per OS)
 └── DeskKit.Core (widget contracts, state model, persistence and pure rules)
```

Dependencies always point down this diagram. In particular, `Core` cannot
reference `App` or `Platform`; a widget cannot call Win32 or reach into another
widget's view model.

## Platform capabilities

`DeskKit.Platform` expresses host integration as capabilities:

| Capability | Contract | Windows implementation | Other platforms |
| --- | --- | --- | --- |
| Desktop placement and deliberate visibility | `IDesktopLayerService` | `WindowsDesktopLayerService` | `NullDesktopLayerService` |
| Window material | `IWindowMaterialService` | `WindowsWindowMaterialService` | `NullWindowMaterialService` |
| Login startup | `IAutoStartService` | `WindowsAutoStartService` | `NullAutoStartService` |
| File icons | `IShellIconLoader` | `WindowsShellIconLoader` | `NullShellIconLoader` |

`AddDeskKitPlatform()` is the only platform selection point used by the app.
New OS support therefore means adding implementations and extending that method,
not adding `OperatingSystem.Is…` branches in views or widgets. A null capability
must be a safe fallback, never an exception for a feature the host can simply
omit.

## Widget SDK and lifecycle

The SDK surface for a widget is deliberately small:

* `IWidgetProvider` describes a stable widget type and creates a view model for
  one placement.
* `WidgetDescriptor` contains only identity, size and interaction policy; UI text
  is a resource key so it can follow a runtime culture change.
* `WidgetContext` supplies placement, versioned settings and `IWidgetHost`.
* `WidgetViewModel` owns its Avalonia controls and is started after its window is
  visible, then stopped and disposed before the window closes.

Providers are contributed through DI. `WidgetRegistry` preserves registration
order and rejects empty or duplicate IDs. This makes built-ins, test widgets and
future plugins use one composition seam. Runtime DLL discovery is intentionally
not part of this boundary until plugin versioning, isolation and trust policies
are specified.

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

`StateStore` is the single writer for `AppState` and SQLite is the source of
truth. It owns EF migrations, the pre-migration backup, old JSON import and the
read-only refusal rules for unavailable or newer schemas. Widget settings are
stored as versioned JSON per placement and migrated by the widget provider. The
shell debounces ordinary writes, but migrations and first-run seeding are saved
immediately.

New host-level data belongs in a typed `AppState`/EF entity migration. New
widget-level data belongs in the widget's settings plus an
`IWidgetSettingsMigrations` step. Neither should create an independent file.

## UI, theme and desktop behaviour

`ThemeService` owns application theme selection. Widgets consume semantic keys
from `WidgetTheme.axaml`; they must not hard-code light or dark content colours.
`IWindowMaterialService` resolves a requested platform material before widget
window layout is calculated, while `WidgetWindow` owns window-only concerns such
as card margins, drag/resize and snap glow. `WidgetShell` coordinates widgets;
it should not accumulate view rendering or native API calls.

## Library decisions

The project already uses Avalonia, CommunityToolkit.Mvvm, EF Core and Microsoft
DI where they reduce framework plumbing. Keep the message bus small and local:
MediatR would add indirection without solving a current cross-process or pipeline
need. If DLL plugins are introduced, evaluate `McMaster.NETCore.Plugins` to avoid
hand-written `AssemblyLoadContext` dependency resolution. For richer settings
validation, evaluate `FluentValidation` only once user-editable settings have
rules that are shared by UI and persistence; annotations or view-only validation
would otherwise split the rule source.
