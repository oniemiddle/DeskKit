# DeskKit 重构计划（最终版）

> **本文件是唯一需要维护的重构方案。** 此前两轮的中间方案（包括 `architecture-review.md` 中的过程性内容）已被本文件取代；被否决的设计不再作为独立方案存在。
> 决策依据见 `architecture-review-final.md`（最终架构结论）。
> **本文件不包含任何已执行的生产代码修改。**

**每个条目标记**：`DECISION`（必须执行）／`OPTIONAL`（可选，需评审确认）／`DO NOT DO YET`（明确暂不做）。

---

## 1. Executive Summary

DeskKit 是一个 **Windows 上基于 Avalonia 的桌面 Widget 宿主**：多个无边框、透明、钉在桌面层（壁纸之上、普通窗口之下、不受 Win+D 影响）的窗口，承载内置 widget（Clock / StickyNote / QuickLaunch）。

**定位**：DeskKit 是 **Framework + Runtime + Product** 的三层组合。

| 层 | 内容 | 现在住在哪里 |
| --- | --- | --- |
| Framework | 稳定契约、共享 Runtime 原语、少量外围能力端口 | `DeskKit.Core`（**但被 EF Core 污染**） |
| Runtime | widget 实例生命周期、窗口表面、placement、吸附、工作区状态、tick | **`DeskKit.App` 内部**（与产品 UI 混居） |
| Product | 组合根、托盘、设置窗口、告警、语言清单、默认布局、单实例、日志、自检 | `DeskKit.App` |

**三个必须解决的问题**（均有代码证据，见第 13 章）：

1. `DeskKit.Core` 同时是 SDK、领域、纯规则与 **EF Core/SQLite 持久化**；`DeskKit.Widgets` 与所有纯规则测试被传递性拖入数据库栈，与 README 的“跨程序集扩展 provider”宣称矛盾。
2. `WidgetShell`（947 行、7 类职责）是 God Object；**Runtime 与 Product 同居一个程序集**，且 `WidgetShell` 直接依赖内置 widget 的本地化程序集。
3. 已确认的边界泄漏：UI 直接引用 `DeskKit.Platform.Windows`；`WidgetWindow` 为自检暴露约 25 个 public 成员。

**方案**：拆成 **6 个项目**（`Core, Runtime, Persistence, Platform, Widgets, App`），`WidgetShell` 拆成 **6 个 Runtime 协作者 + 3 个 Product UI + 1 个门面**，并把持久化与纯规则**按数据所有权**归位。**不**引入插件加载、MediatR/CQRS、`Application`/`UI` 项目、`Platform.Windows` 拆分、窗口接口。

> 最终收益：`Runtime ──→ Core` 成为唯一依赖（本轮已验证可达），第三方 widget 只需 Core，产品 UI 可脱离 Runtime 具体类型测试。

---

## 2. Baseline 与证据分级

必须严格区分两类证据；**不得把历史 artifact 写成“本次验证通过”**。

### 2.1 Historical Evidence（仓库既有，非本轮产生）

| 证据 | 内容 |
| --- | --- |
| `artifacts/selftest-report.txt` | 21 个章节，**203 PASS / 0 FAIL**；环境 Windows NT 10.0.26200.0；报告头时间 `2026-10-02 16:55:50 +08:00` |
| `docs/architecture.md`、`README.md` | 设计意图与产品行为描述 |
| `.github/workflows/ci.yml` | `windows-latest`：restore → build Release → test。**CI 不执行自检** |

### 2.2 Current Verification（本轮 / 本会话实际执行）

- 只读：全部 `*.csproj`/`*.slnx`/`Directory.*.props`/`global.json`/`.config/dotnet-tools.json`/CI/`README.md`/`docs/architecture.md` 阅读。
- `src/` 4 个项目全部源文件逐文件阅读（约 100 个 `.cs`/`.axaml`）。
- 递归 `Select-String` 检索：跨项目 `using`、`Seed`/`AddWidget`/`IWidgetHost`/`Context.Placement`、平台类型消费者、成员可见性统计、`Title` 读取点。

### 2.3 执行状态（P0 已关闭）

三项均已在本会话 **P0 阶段实际执行**（`2026-10-02 18:12–18:14`），结果见 §2.6。此前“被 plan mode 阻断”的记录保留在 §2.2 的历史说明中，仅用于说明为什么本文件早期版本没有结果。

> 第 23 章的验证矩阵从 P1 起生效；P0 的出口条件（真实基线）已满足。

### 2.4 环境（P0 必须记录）

| 项 | 值 |
| --- | --- |
| `global.json` 固定 SDK | `10.0.100`，`rollForward: latestMajor`，`allowPrerelease: true` |
| 本机 `dotnet --version` | **`11.0.100-rc.1.26425.128`** |
| 说明 | 使用中的是 **.NET 11 RC**，而非 `global.json` 声明的 .NET 10。这是**现有 `rollForward: latestMajor` + `allowPrerelease: true` 配置导致的既有环境差异**，不是本计划引入的；baseline 数字必须连同该事实一起记录，否则无法判断后续构建/测试差异来自代码还是 SDK |
| Git HEAD / 工作区 | `2a5c4d3`；`git status --porcelain` **为空**（干净；`bin/`、`obj/`、`artifacts/` 由 `.gitignore` 忽略） |
| 构建/测试配置 | build 与 test 使用 **Release**；selftest 按 §2.5 的原样命令运行，因此落在 **Debug**（`src\DeskKit.App\bin\Debug\net10.0\DeskKit.App.exe`）。§2.6 已如实记录该差异 |

### 2.5 P0 命令序列（按序执行，结果填入 2.6）

```powershell
dotnet build DeskKit.slnx -c Release
dotnet test  DeskKit.slnx -c Release
dotnet run --project src/DeskKit.App -- --selftest --out artifacts/selftest-report.txt
```

### 2.6 P0 结果（**已执行完成 — 基线确认**）

执行时间：`2026-10-02 18:12–18:14`（+08:00）| commit `2a5c4d3` | 工作区干净。

| 项 | 命令 | 结果 |
| --- | --- | --- |
| **build** | `dotnet build DeskKit.slnx -c Release` | **成功**：`0 个警告 / 0 个错误`，用时 35.63 s（6 个项目全部还原并生成；仅出现 `NETSDK1057` 预览 SDK 提示消息，不计为警告） |
| **test** | `dotnet test DeskKit.slnx -c Release --no-build` | **全部通过**：`DeskKit.Core.Tests` **148 通过 / 0 失败 / 0 跳过**（5 s）、`DeskKit.Platform.Tests` **41 通过 / 0 失败 / 0 跳过**（202 ms）→ **合计 189 通过 / 0 失败 / 0 跳过** |
| **selftest** | `dotnet run --project src/DeskKit.App -- --selftest --out artifacts/selftest-report.txt` | **PASS**：`RESULT: PASS (203 checks)`，**退出码 0**，21 个章节，报告内 **0 处 FAIL**；报告 23 662 bytes |

**与 Historical Evidence 的对照**

| | 历史 artifact | 本次（P0） |
| --- | --- | --- |
| 报告时间 | `2026-10-02 16:55:50 +08:00` | `2026-10-02 18:13:44 +08:00` |
| 结果 | `RESULT: PASS (203 checks)` | `RESULT: PASS (203 checks)` |
| 章节数 / FAIL | 21 节 / 0 FAIL | 21 节 / 0 FAIL |
| 报告大小 | 23 543 bytes | 23 662 bytes（差异来自时间戳、窗口句柄、时间测量值） |
| 二进制配置 | 未记录 | **Debug**（见 §2.4） |

**环境差异（必须随基线一起记录）**

- `global.json` 声明 SDK **10.0.100**（`rollForward: latestMajor`、`allowPrerelease: true`）。
- 实际使用 **`11.0.100-rc.1.26425.128`**（.NET 11 RC）——**既有的 roll-forward / allowPrerelease 配置导致，非本计划引入**。
- 影响：构建产物仍为 `net10.0`（`Directory.Build.props` 的 `TargetFramework`），但编译器/Roslyn 来自 SDK 11 RC。后续若出现与本次基线不一致的构建或测试结果，**先排除 SDK 差异**，再判断是否由重构引起。

**结论**：基线通过，P0 出口条件满足 → **可以进入 P1**。历史报告中“真实数据库未被触碰”的断言在本次同样成立（`the real database was not touched by any of this`）。

---

## 3. Current Architecture（现状，以代码为准）

### 3.1 物理组成

```
DeskKit.slnx                  /src/ 4 项目 + /tests/ 2 项目
Directory.Build.props         net10.0 / nullable / ImplicitUsings / EnforceCodeStyleInBuild / 编译绑定默认开启
Directory.Packages.props      中央包版本（Avalonia 12.1.3、EF Core 10.0.12、xUnit 2.9.3…）
global.json                   SDK 10.0.100

src/DeskKit.Core/        2351 行  39 文件  Widget 契约 + 领域模型 + 纯规则 + EF/SQLite 持久化
src/DeskKit.Platform/    1682 行  18 文件  桌面层/材质/自启动/Shell 图标实现 + 纯策略
src/DeskKit.Widgets/      964 行  25 文件  三个内置 widget + 自己的本地化资源
src/DeskKit.App/         5013 行  26 文件  组合根 + 宿主编排 + UI + 诊断自检
tests/DeskKit.Core.Tests/     1695 行 12 文件
tests/DeskKit.Platform.Tests/  290 行  2 文件
scripts/   New-AppIcon.ps1（生成 ico）、Publish.ps1（self-contained 发布）
```

### 3.2 逻辑职责落点（提示中的分类）

| 职责 | 当前落点 |
| --- | --- |
| Domain / 核心概念 | `Core/Models`（`AppState`、`AppSettings`、`WidgetPlacement`、`ScreenBounds`、`ThemeSetting`、`LanguageSetting`） |
| Widget contract | `Core/Abstractions` + `Core/Models`（`IWidgetProvider`、`WidgetViewModel`、`WidgetContext`、`WidgetDescriptor`、`IWidgetHost`、`IWidgetMessageBus`、`ITickAware`） |
| Widget runtime | `App/Services/WidgetShell.cs` + `WidgetRuntime.cs` + `TickService.cs` |
| Application orchestration | `App/Services/WidgetShell.cs`、`App.axaml.cs`（无独立 application 层） |
| UI | `App/Views/*`、`App/ViewModels/*` |
| Persistence | `Core/Data/*` + `Core/Services/StateStore.cs` + `LegacyJsonImport.cs` + `AppPaths.cs` |
| Platform integration | `Platform/*`（`Windows/*`、`Interop/*`、`*Policy.cs`） |
| Built-in Widgets | `Widgets/*` |
| Diagnostics / Logging | 日志：`App.axaml.cs`（Serilog）；诊断：`App/Diagnostics/*`（约 2600 行） |
| Configuration | `Core/Models/{AppSettings,ThemeSetting,LanguageSetting}` + `Core/Data/SettingsEntity` |
| Infrastructure | 分散：`AppPaths`、`DatabaseOptions`、`SqliteConnectionPragmas`、`SingleInstanceGuard`、`ScreenProbe` |
| Composition Root | `App.axaml.cs::BuildServices()` + `Platform/PlatformServiceCollectionExtensions.AddDeskKitPlatform()`（两处） |

---

## 4. Repository Structure Analysis（现状）

### 4.1 项目引用（实测）

```
DeskKit.App      → Core, Platform, Widgets
DeskKit.Platform → Core
DeskKit.Widgets  → Core
DeskKit.Core     → （无项目引用）
Core.Tests       → Core
Platform.Tests   → Core, Platform
```

**无环。** 与 `docs/architecture.md` 的“Dependencies always point down this diagram”一致。

### 4.2 NuGet（实测）

| 项目 | 包 | 备注 |
| --- | --- | --- |
| Core | Avalonia, CommunityToolkit.Mvvm, **Microsoft.EntityFrameworkCore.Sqlite**, EF Core Design, Logging.Abstractions | **问题：Core 直接持有数据库栈** |
| Platform | Avalonia, Logging.Abstractions, DI.Abstractions | 为返回 `Bitmap` 而引用 Avalonia |
| Widgets | Avalonia, CommunityToolkit.Mvvm, Irihi.Lingua, DI.Abstractions | 无 EF，但经 Core 传递获得 |
| App | Avalonia(+Desktop/Fluent/Fonts/DiagnosticsSupport), CommunityToolkit.Mvvm, Irihi.Lingua, DI, Logging, Serilog(+Sinks.File) | 组合根 |
| Tests | xunit + Test.Sdk + coverlet | 无 `Avalonia.Headless` |

### 4.3 保留项

`Directory.Packages.props` 中央版本、`Directory.Build.props` 统一配置、`.slnx` 新格式、`scripts/` 中“资源由脚本可复现生成”的做法 —— **全部保留**。

---

## 5. Dependency Analysis（现状）

### 5.1 分类回答

| 关注点 | 承担方 |
| --- | --- |
| 依赖 Avalonia | 全部 4 个 src 项目。Core 依赖它是**契约使然**（`CreateView(): Control`） |
| 依赖 EF Core / SQLite | 仅 Core（经 `StateStore`/`Data/*`/`LegacyJsonImport`），**但被所有人传递依赖** |
| 依赖 Windows API | `Platform`（`Interop/*`+`Windows/*`）；**外加** `App/Views/NoticeWindow.cs:5` 直接 `using DeskKit.Platform.Windows` |
| 依赖 MVVM | Core（`WidgetViewModel`）、Widgets、App |
| DI composition | `App.BuildServices()` + `Platform.AddDeskKitPlatform()`（两处） |
| Widget 生命周期 | `WidgetShell` |
| 窗口生命周期 | `WidgetShell` 创建；`WidgetWindow.OnOpened/OnClosed` 做 `Attach/Detach` |
| 持久化 | `StateStore`（唯一磁盘写入者） |
| 状态 | 分裂：磁盘 `StateStore`；内存 `WidgetShell.State`；widget 自身 `WidgetSettings` |
| 布局 / Snap / Placement | 纯算法在 `Core/Models`、`Core/Services/PlacementNormalizer`；**编排在 `WidgetShell.SnapPosition/CapturePlacement`**；窗口↔卡片换算在 `WidgetWindow` |
| 平台特性 | `Platform`（含纯策略 `DesktopLayerPolicy`/`MaterialPolicy`），由 `AddDeskKitPlatform()` 选择 |

### 5.2 隐性耦合（真实存在）

1. `App/Views/NoticeWindow.cs:5` → `DeskKit.Platform.Windows`（生产 UI 泄漏）。
2. `App/Services/WidgetShell.cs:13` → `DeskKit.Widgets.Clock`（播种默认 widget）。
3. **`App/Services/WidgetShell.cs:14` → `DeskKit.Widgets.Localization`**（`WidgetText.Value/Observable` 用于 `Widgets` 投影、`CreateWidget` 的窗口标题、`CreateTrayIcon` 菜单项）—— 比第 2 条更硬：**Runtime 若要复用，就不能依赖内置 widget 程序集**。
4. `App/ViewModels/SettingsViewModel.cs` 直接依赖具体类 `WidgetShell`。
5. 契约放置不一致：`IShellIconLoader` 在 `Core/Abstractions`；`IDesktopLayerService`/`IWindowMaterialService`/`IAutoStartService`/`WidgetMaterial` 在 `Platform` 根。

---

## 6. Core Domain / Concept Analysis（现状）

### 6.1 Widget contract

- `IWidgetProvider`：`Descriptor` + `Create(WidgetContext) → WidgetViewModel`。极小，设计良好。
- `WidgetContext`：`Placement` + `Settings` + `IWidgetHost` + `InstanceId` —— **最干净的 seam**（widget 只看到 `IWidgetHost`）。
- `WidgetViewModel`：`ObservableObject` + `CreateView(): Control`、`CreateSettingsView(): Control?`、`Start/Stop/Dispose`。契约依赖 Avalonia + MVVM（**有意为之**，见 D-1）。
- `IWidgetHost`：`Messages`、`Screens`、`ShowSettings(vm)`、`RemoveWidget(vm)`、`RequestSave()`。最小且合理。
- `WidgetDescriptor`：不可变 record；名称是**资源键**（为了运行时切语言）。

### 6.2 状态 / 设置 / 持久化

- `AppState`（`Settings` + `List<WidgetPlacement>`）是唯一持久化根；`WidgetPlacement` 同时承载位置/尺寸/启用/设置版本/不透明设置。
- `WidgetSettings`：`Dictionary<string, JsonElement>` + 反射式 `Get<T>/Set<T>`；**对宿主不透明**。设计正确。
- `SettingsMigrations` / `IWidgetSettingsMigrations` / `WidgetSettingsMigration`：widget 自持版本历史，宿主只按顺序执行不解读，且**整链先校验再执行**。**保留原样。**

**混入的非领域内容**：`StateStore`、`DeskKitDbContext`、`DatabaseOptions`、`SqliteConnectionPragmas`、`DatabaseBackup`、`LegacyJsonImport`、`AppPaths` —— 这些是**持久化实现**。

### 6.3 纯规则（好设计，保留）

`WidgetSnapEngine`、`WidgetDragSession`、`WidgetResizeSession`、`WidgetGlowSegment`、`PlacementNormalizer`、`ThemeSetting`、`LanguageSetting`、`SettingsMigrations`。

### 6.4 其它概念

| 概念 | 落点 | 评价 |
| --- | --- | --- |
| Material | 契约+`MaterialPolicy`（Platform）、Windows 实现、消费方 `WidgetShell`/`WidgetWindow`/`ThemeService` | 策略纯函数化，好 |
| AutoStart | `IAutoStartService`+`WindowsAutoStartService`+`NullAutoStartService`；协调规则在 `WidgetShell.ReconcileAutoStart` | 规则是纯逻辑却埋在 God Object，应抽出 |
| Theme | `ThemeService`（App）+ `ThemeSetting`（Core）+ `WidgetTheme.axaml`（Widgets） | `CardBrushFor` 为 `static` 且读 `Application.Current`，难测 |
| Localization | `AppLanguage`（App 资源）+ `WidgetLanguage`（Widgets 资源），由 `LanguageService` 统一驱动 | 分层合理；widget 名称映射为手写常量表，无校验 |
| Tray | `WidgetShell.CreateTrayIcon` | 属 UI，却由 God Object 承担 |
| Notice | `INoticePresenter`/`NoticePresenter` + `NoticeWindow`（直接引用 `Platform.Windows`） | presenter 抽象好；`NoticeWindow` 有泄漏 |
| Tick | `TickService`：单一定时器驱动全部 `ITickAware` | **保留** |

---

## 7. WidgetShell / Large Object Analysis

### 7.1 规模

`src/DeskKit.App/Services/WidgetShell.cs`：**947 行，17 public / 44 private 成员，单类**，`sealed`，实现 `IWidgetHost, IDisposable`。仓库唯一的 God Object。

### 7.2 字段按职责分类

| 职责 | 字段 |
| --- | --- |
| 持久化 / 状态 | `_stateStore`、`State`、`_saveTimer`、`StateChanged` |
| 目录 / 运行时 | `_registry`、`_widgets`、`Widgets`、`AvailableWidgets`、`Runtimes` |
| 放置 / Snap | `_material`、`_surfaceMargin`、`SnapGap`、`SnapThreshold` |
| 平台 | `_desktopLayer`、`_autoStart`、`_materials` |
| 调度 | `_tickService` |
| 外观 / 本地化 | `_themeService`、`Language` |
| Shell UI | `_trayIcon`、`_settingsWindow`、`_appIcon`、`_notices` |
| Host 契约 | `_messages` |
| 诊断 | `_logger` |

### 7.3 方法按职责分类

| 职责组 | 方法 |
| --- | --- |
| Host 契约 | `Screens`、`Messages`、`ShowSettings`、`RemoveWidget`、`RequestSave` |
| 生命周期 | `Start`、`Dispose` |
| Widget 管理 | `AddWidget`、`CreateWidget`、`DestroyWidget`、`Remove`、`FindRuntime`、`CreatePlacement`、`WindowSizeForPlacement`、`CapturePlacement` |
| 放置 / Snap | `SnapPosition`、`ClearSnapHighlights`、`OnDragCompleted`、`WindowScaling`、`TryGetCardSize`、`TryGetCardRect` |
| 持久化 / 存储策略 | `ScheduleSave`、`SaveNow`、`SeedDefaultWidgets`、`MigrateWidgetSettings`、`ShowStorageNotice`、`StoredDataIsNewer` |
| 设置协调 | `ApplySettings`、`SetWidgetsVisible`、`ReconcileAutoStart` |
| Shell UI | `BuildContextMenu`、`OpenSettings`、`CreateSettingsWindow`、`CreateTrayIcon`、`GetAppIcon` |

**证据**：`WidgetShell` 同时 `using` `Avalonia.Controls`（`ContextMenu`/`NativeMenu`/`TrayIcon`/`WindowIcon`）、`Avalonia.Threading`（`DispatcherTimer`）、`DeskKit.App.Views`（`WidgetWindow`/`SettingsWindow`）、`DeskKit.Platform`、`DeskKit.Widgets.Clock`、`DeskKit.Widgets.Localization`。

### 7.4 其它大型类

| 类 | 行数 | 问题 |
| --- | --- | --- |
| `App/Views/WidgetGlowLayer.cs` | 652 | 自绘发光；20 public 成员 |
| `App/Views/WidgetWindow.axaml.cs` | 438 | **51 public 成员，其中约 25 个为自检而生**（注释多写 “Exposed so the self-test can…”）；同程序集内改 `internal` 即可 |
| `App/Diagnostics/DesktopLayerSelfTest*.cs` | 426+334+600+787 ≈ 2147 | 诊断直接访问 shell internals 与 EF/Sqlite；属生产程序集（**保持**，见 D-13） |

### 7.5 直接依赖内置 Widget 的证据

- `WidgetShell.cs:354`：`_registry.Find(ClockWidgetProvider.WidgetId)`（首启播种）。
- `WidgetShell.cs:109 / 438 / 833`：`WidgetText.Value/Observable`（`Widgets` 投影、窗口标题、托盘菜单）。
- `App.axaml.cs:164` 与 `BuiltInWidgets.AddBuiltInWidgets()` 之外，`BuiltInWidgets.CreateProviders()` 是自检使用的第二条注册路径。

---

## 8. Persistence Analysis

### 8.1 现状

单文件 SQLite：`%LOCALAPPDATA%\DeskKit\deskkit.db`（`AppPaths.DatabasePath`），WAL + `synchronous=FULL` + `busy_timeout=3000`。三张表：

| 表 | 行 | 内容 |
| --- | --- | --- |
| `Settings` | 单行（`SingletonId=1`） | Theme / Language / StartWithWindows / ShowTrayIcon / WidgetsVisible / DesktopDoubleClickTogglesWidgets / WidgetsAnimation / WidgetAnimationSpeed / WidgetAnimationDirection / WidgetAnimationEasing |
| `Widgets` | 每实例一行 | `InstanceId`(PK) / `Order` / `WidgetId` / `Enabled` / X,Y（物理像素）/ Width,Height（逻辑像素）/ `SettingsVersion` / `SettingsJson`（不透明 JSON） |
| `Meta` | key/value | 仅 `imported-from`（诊断） |

旧 JSON（`%APPDATA%\DeskKit\`）由 `LegacyJsonImport` **只读一次**导入。

### 8.2 四类数据必须分开

| 类别 | 载体 | 归属 |
| --- | --- | --- |
| Runtime state | `WidgetShell.State`、`WidgetRuntime.IsVisible`、窗口实时几何 | 宿主运行时，**不持久化** |
| User Settings | `AppSettings` ↔ `SettingsEntity` | Product 定义语义，宿主持久化 |
| Widget State | `WidgetPlacement.Settings` ↔ `WidgetEntity.SettingsJson` + `SettingsVersion` | **Widget**，宿主不解读 |
| Database Entity | `SettingsEntity`/`WidgetEntity`/`MetaEntity` | 持久化实现细节，**不得**出现在 Core/Runtime 之外 |

### 8.3 `WidgetPlacement` 与 `WidgetEntity` 的映射（DECISION-12）

```
WidgetPlacement（Runtime 领域记录）  ↕  WidgetEntity（EF Entity）
```

- 映射**只**发生在 `StateStore.Read/Write`（现状已如此），拆分后仍只在 Persistence 内。
- Core/Runtime **不得**引用 `WidgetEntity`/`DeskKitDbContext`。
- `WidgetPlacement.Settings`（`Dictionary<string,JsonElement>`）↔ `SettingsJson` 文本，序列化在 Persistence。

### 8.4 保留的持久化保证（不要动）

- WAL + `synchronous=FULL`；备份必须走 SQLite backup API（`DatabaseBackup` 注释 + `StateStoreTests` 覆盖 WAL 场景）。
- “更新 schema → 不读不写 + 告警后退出”“无法打开 → 只读 + 告警”。
- Widget 设置迁移链的“整链先校验 / 缺步骤不推进 / 更新版本原样保留”。
- **EF 迁移 Id**：`20261002062527_InitialCreate`（不含命名空间）→ 命名空间迁移不影响已应用数据库。

---

## 9. Platform Analysis

### 9.1 现状能力与实现

| 能力 | 契约位置 | Windows | Null | 纯策略 |
| --- | --- | --- | --- | --- |
| 桌面层钉底 | `Platform/IDesktopLayerService.cs` | `Windows/WindowsDesktopLayerService.cs` | `NullDesktopLayerService.cs` | `DesktopLayerPolicy.cs` |
| 桌面双击手势（后续新增） | `Core/Abstractions/IDesktopGestureService.cs` | `Windows/WindowsDesktopGestureService.cs` | 同文件 | `DesktopBackdropPolicy.cs`、`DesktopDoubleClickDetector.cs` |
| 窗口材质 | `Platform/IWindowMaterialService.cs` | `Windows/WindowsWindowMaterialService.cs` | `NullWindowMaterialService.cs` | `MaterialPolicy.cs` |
| 自启动 | `Platform/IAutoStartService.cs` | `Windows/WindowsAutoStartService.cs` | 同文件 | — |
| Shell 图标 | **`Core/Abstractions/IShellIconLoader.cs`** | `Windows/WindowsShellIconLoader.cs` | 同文件 | — |

选择点唯一：`PlatformServiceCollectionExtensions.AddDeskKitPlatform()`。**保留。**

### 9.2 归属规则（DECISION-6，取代第一轮“统一下沉”）

| 类型 | 判定 | 归属 |
| --- | --- | --- |
| `IDesktopLayerService`（+`DesktopLayerOptions`） | 运行时消费的能力端口 | **Core** |
| `IWindowMaterialService` | 运行时消费的能力端口 | **Core** |
| `WidgetMaterial` | 运行时与平台之间的**词汇表** | **Core** |
| `MaterialPolicy.FillsWindow` | `WidgetMaterial` 的纯属性，无 OS 知识 | **Core**（作为 `WidgetMaterial` 上的方法） |
| `MaterialPolicy.Resolve` + Windows build 常量 | 编码 Windows 版本策略（22621/22000） | **留 Platform** |
| `DesktopLayerPolicy` | WndProc 钉底规则；Runtime 不消费 | **留 Platform** |
| `IDesktopGestureService` | 产品消费的能力端口（App 把事件变成 `SetWidgetsVisible`，与托盘同一条命令） | **Core** |
| `DesktopBackdropPolicy` / `DesktopDoubleClickDetector` | 桌面命中判定与双击识别规则；Runtime 不消费 | **留 Platform** |
| `ScreenBoundsMapper` | Avalonia `Screens` → Core `ScreenBounds` 桥接 | **移到 Runtime** |
| `IShellIconLoader`(+Null) | **Widgets 消费** | **Core（已如此）** |
| `IAutoStartService`(+Null) | 产品能力，与 widget 运行无关 | **留 Platform** |
| `INotificationWindowStyler`(+Null) | 修 `NoticeWindow` 泄漏 | **留 Platform 根** |

**禁止**：不把 Windows 版本号、`Interop`、Win32 常量搬进 Core。

### 9.3 不拆 `Platform.Windows`（N-3）

只有一套实现；Null 实现已覆盖其它平台；`LiquidGlass` 明确未实现；`AddDeskKitPlatform()` 已是唯一 OS 分支点。

### 9.4 已确认的平台泄漏（要修）

- `App/Views/NoticeWindow.cs:5` `using DeskKit.Platform.Windows;` + `OnOpened` 中的 `OperatingSystem.IsWindows()` 分支 → 新增 `INotificationWindowStyler` 契约（Platform 根）+ `WindowsNotificationWindowStyler`（Platform.Windows）+ `NullNotificationWindowStyler`，由组合根注入。
- `App/Diagnostics/*` 使用 `Platform.Windows.DesktopDiagnostics`：**允许并记录**（D-13）。

---

## 10. UI Architecture Analysis

- **宿主窗口**：`WidgetWindow`（`WindowDecorations=None`、`TransparencyLevelHint=Transparent`、`CanResize=False`）→ `CardBorder`（`ContentPresenter` + `DragHandle`/`DragBar` + `WidgetGlowLayer`）。窗口↔卡片换算：`WindowSizeForCard`/`CardSizeForWindow`/`MarginFor(material)`。
- **设置窗口**：`SettingsWindow.axaml`（`x:DataType=vm:SettingsViewModel`，编译绑定）+ `SettingsViewModel`（Appearance/Startup/Widgets；直接读写 `WidgetShell` 实时状态）。
- **告警窗口**：`NoticeWindow`（自绘表面 + 通知样式）。
- **发光**：`WidgetGlowLayer`（自绘 `Control`）。

**观察**：

1. `SettingsViewModel` → 具体 `WidgetShell`：UI 层可测性的主要障碍（→ `IShellFacade`）。
2. UI 构造（托盘/右键菜单/设置窗口）散布在编排类里，本地化订阅（`AppLanguage.Instance.X.SubscribeAction`）也混在其中。
3. `ThemeService` 是唯一主题决策点（好），但 `CardBrushFor`/`FloatingSurface`/`NoticeBorderBrush` 为 `static` 且读全局 `Application.Current`（难测，O-4）。
4. 视图为 code-behind + 少量 MVVM：**不重写**（N-5）。
5. `WidgetViewModel.CreateSettingsView()` 返回 `Control?`，设置面板生命周期由 `SettingsViewModel.OnSelectedWidgetChanged` 驱动，方向正确。

---

## 11. Testing Analysis

### 11.1 现有测试（12 + 2 文件）

| 测试文件 | 覆盖 | 性质 |
| --- | --- | --- |
| `Core.Tests/LanguageSettingTests`、`ThemeSettingTests`、`PlacementNormalizerTests` | 纯逻辑 | 纯逻辑 |
| `Core.Tests/WidgetSnapEngineTests`(19)、`WidgetDragSessionTests`、`WidgetResizeSessionTests`(18)、`WidgetGlowSegmentTests` | 几何 | 纯逻辑 |
| `Core.Tests/WidgetSettingsTests`、`SettingsMigrationsTests`、`WidgetRegistryTests`、`WidgetMessageBusTests` | 机制 | 纯逻辑 |
| **`Core.Tests/StateStoreTests`（19+）** | 真实 SQLite：首启/往返/拒绝写入/导入/备份 WAL/迁移 | **依赖数据库/文件系统** |
| `Platform.Tests/DesktopLayerPolicyTests`、`MaterialPolicyTests` | 平台决策纯逻辑 | 纯逻辑 |

### 11.2 可测试性结论

- **可测且已测**：全部纯规则。
- **依赖 Avalonia**：`WidgetViewModel.CreateView()`、`WidgetGlowLayer`、`WidgetWindow`（**无测试**）。
- **依赖 Windows**：`Windows*` 实现与钉底（**无单测**，靠自检）。
- **依赖数据库**：`StateStore`（有真实库测试）。
- **因架构而无测试的核心逻辑**：`WidgetShell` 的启动顺序/播种/自启动对齐/吸附编排、`SettingsViewModel`、`ThemeService`、`LanguageService`、`TickService`、窗口↔卡片换算、`WidgetDescriptor` 资源键是否真实存在。

### 11.3 边界建立后可新增的高价值测试

| 新边界 | 测试 |
| --- | --- |
| `WidgetSettingsMigrator`（Core） | 成功推进/缺步骤/更新版本/重复步骤/混合实例 |
| `AutoStartReconciliation`（Core） | 不支持/一致/不一致 |
| `WindowCardGeometry`（Core） | 往返一致、`margin=0`、下限保护 |
| `WidgetSeedPolicy`（Core） | 按 `DefaultLayout` 播种、已有状态不播种、用户删除后不恢复 |
| `IStateStore`（Core 契约） | `WorkspaceState` 用 fake store 测节流/合并/拒绝写入 |
| `IShellFacade`（Runtime 契约） | `SettingsViewModel` 用 fake facade 测标签刷新/语言切换/选择映射 |
| `PlacementController` | 用 fake `ICardRectSource` 测吸附 + 发光段 |
| 资源键完整性 | 对 `BuiltInWidgets.CreateProviders` 每个 descriptor，断言名称/描述键在两个 `Strings*.json` 中都存在 |

---

## 12. Design Principles for the Refactor

1. **Core 的依赖面 = 稳定契约 + 共享原语 + 少量能力端口所需**（不携带 ORM，也不携带产品决策）。
2. **按数据所有权 + 生命周期划分，而不是按层次划分**。
3. **纯规则下沉 Core，编排留在 Runtime，产品决策留在 App**。
4. **不为了架构增加无意义 abstraction**。判断标准：**“这个接口如果删掉，会失去什么架构能力？”答不出就不加。**
5. **不牺牲封装换测试**：优先 `internal` 与注入数据，而非公开内部状态。
6. **不改没有必要的公共 API**；只做加法（必要时）。
7. **每一步可回退、可验证**：每阶段结束 `dotnet test` 全绿，并在开发机上重新运行自检。
8. **先理解 DeskKit，再设计 DeskKit**：不引入仓库没有需求的模式。

---

## 13. Current Architecture Problems

| ID | 问题 | 证据 | 后果 | 级别 |
| --- | --- | --- | --- | --- |
| **P-1** | Core 把 EF Core/SQLite 强加给所有人 | `Core.csproj` 引用 `Microsoft.EntityFrameworkCore.Sqlite`；`StateStore`/`Data/*` 在 Core；`Widgets.csproj → Core` | Widgets 与全部纯规则测试被传递性拖入数据库栈；README 宣称的“跨程序集扩展”要求插件也接受该依赖 | Must |
| **P-2** | `WidgetShell` 是 God Object（947 行 / 7 类职责） | 第 7.2/7.3 分组；其 `using` 跨越 UI/平台/持久化/内置 widget | 任何改动都在同一个类；无法脱离真实窗口/数据库测试 | Must |
| **P-3** | Runtime 与 Product 同居 `DeskKit.App` | 5013 行中 Runtime≈2126、Product≈2887，且 `WidgetShell` 同时引用两者 | Runtime 不可复用、不可独立版本化；会持续膨胀 | Must |
| **P-4** | Runtime 依赖内置 widget 的本地化程序集 | `WidgetShell.cs:14` `using DeskKit.Widgets.Localization`；用于 3 处 | 复用 Runtime 必带上三个内置 widget | Must |
| **P-5** | 宿主直接依赖具体内置 widget | `WidgetShell.cs:13,354` `ClockWidgetProvider.WidgetId` | 宿主并非“只认识 `IWidgetProvider`”；默认播种硬编码 | Should |
| **P-6** | UI 直接依赖 Windows 实现 | `NoticeWindow.cs:5` | 与 `docs/architecture.md` 的“视图中无 `OperatingSystem.Is…` 分支”自相矛盾 | Should |
| **P-7** | `WidgetWindow` 为自检暴露约 25 个 public 成员 | 51 public；同程序集 | 生产控件公开面被测试需求绑架 | Should |
| **P-8** | 状态所有权文档化不实 | docs 称 `StateStore` 是 `AppState` 唯一所有者；实际 `WidgetShell.State` 持有并修改 | 节流/拒绝写入/迁移写回规则没有单一权威描述 | Should |
| **P-9** | 契约放置不一致 + 两处组合根 | `IShellIconLoader` 在 Core；其余平台契约在 Platform | 消费者无法只依赖契约；削弱 `AddDeskKitPlatform()` 的价值 | Should |
| **P-10** | 内置 widget 本地化键无校验 | `WidgetText` 手写常量表；`Value(key)` 找不到时返回 key 本身 | 漏键不会编译/测试失败，UI 出现 `Widget_X_Name` | Should |
| **P-11** | `WidgetContext.Placement` 语义未定义 | `CapturePlacement` 替换 `WidgetRuntime.Placement`，`Context` 不更新；全仓库无人读取 | 潜在语义陷阱（首个读取它的第三方 widget 会拿到过期坐标） | Should（锁定语义即可） |
| **P-12** | `ThemeService` 静态全局依赖 | `CardBrushFor`/`FloatingSurface`/`NoticeBorderBrush` 为 `static` 且读 `Application.Current` | 主题映射难测 | Could（O-4） |

### 13.1 文档与代码不一致清单（P5 阶段修正）

| 文档 | 代码事实 | 处理 |
| --- | --- | --- |
| `docs/architecture.md` 把 Core 描述为“contracts, state model, persistence and pure rules”（一个盒子） | 四类职责同居一程序集 | 拆为 Core / Runtime / Persistence 三个盒子 |
| “`StateStore` is the single writer for `AppState`” | 内存态属 `WidgetShell` | 改为“StateStore 是唯一磁盘写入者；`WorkspaceState` 拥有内存态” |
| “`WidgetShell` … should not accumulate view rendering or native API calls” | 无原生调用属实，但它在构造 TrayIcon/ContextMenu/SettingsWindow | 改为“门面只做编排；UI 构造在 Product 控制器” |
| README“the shell only knows about `IWidgetProvider`” | `WidgetShell` 依赖 `ClockWidgetProvider` 与 `WidgetText` | 以代码为准，改文档并在 P4/P5 移除依赖 |
| README“Adding a widget”第 3 步 | 无校验保证键存在 | 增加资源键完整性测试 |
| README/架构文档“future plugin loader” | 当前无任何加载机制 | 明确保留扩展点但**未实现**（N-1） |

---

## 14. Target Architecture（DECISION）

### 14.1 三层

```
Framework  →  Runtime  →  Product / App
```

| 层 | 职责 | 项目 |
| --- | --- | --- |
| Framework | 稳定契约、共享 Runtime 原语、少量外围能力端口 | `DeskKit.Core` |
| Runtime | widget 实例生命周期、窗口表面、placement、吸附、工作区状态、tick、运行时外观 | `DeskKit.Runtime` |
| Product | 组合根、托盘、设置、告警、应用本地化、默认布局、单实例、日志、自检 | `DeskKit.App` |
| （支撑） | 存储实现 | `DeskKit.Persistence` |
| （支撑） | OS 能力实现 + 策略 + 选择 | `DeskKit.Platform` |
| （内容） | 官方内置 widget | `DeskKit.Widgets` |

### 14.2 各项目“为什么必须独立”

- `Core`：**稳定契约 + 共享 Runtime 原语 + 少量外围能力端口**（见 14.4）。它不含 EF / Win32 / 产品 UI，也不含产品决策；它存在的理由是给 `Runtime`、`Widgets` 与 `Persistence`/`Platform` 提供**共同、稳定、无实现细节**的依赖面。
- `Runtime`：把“运行 widget”与“官方产品界面”分开，使宿主引擎可复用、可内部演化（论证见第 15.1 节）。
- `Persistence`：唯一持有 ORM/驱动，使 Core/Runtime 的依赖面可证明干净。
- `Platform`：唯一 OS 实现与选择点。
- `Widgets`：官方内置 widget，与第三方同构，不含宿主逻辑。
- `App`：官方交付物与组合根。

### 14.3 目标目录结构

```
src/
  DeskKit.Core/                    # 稳定契约 + 共享 Runtime 原语 + 少量外围能力端口（Avalonia + CommunityToolkit.Mvvm + Logging.Abstractions）
    Abstractions/                  # IWidgetProvider, IWidgetMessageBus/IWidgetMessage, IShellIconLoader(+Null),
                                   # IStateStore, StoreOutcome 家族, INoticePresenter(+Null),
                                   # IDesktopLayerService(+DesktopLayerOptions), IWindowMaterialService, WidgetMaterial
    Models/                        # WidgetViewModel/WidgetContext/WidgetDescriptor/IWidgetHost/ITickAware,
                                   # AppState, AppSettings, WidgetPlacement, ScreenBounds, WidgetSnapEngine,
                                   # WidgetDragSession, WidgetResizeSession, WidgetGlowSegment, WidgetMaterialExtensions,
                                   # ThemeSetting, LanguageSetting, AutoStartReconciliation(新), WindowCardGeometry(新)
    Services/                      # WidgetRegistry, WidgetSettings, SettingsMigrations, WidgetSettingsMigrator(新),
                                   # WidgetSeedPolicy(新), PlacementNormalizer
    ObservableAction.cs
  DeskKit.Runtime/                 # 只依赖 Core
    WidgetShell.cs                 # facade: IWidgetHost + IShellFacade + 启动顺序
    IShellFacade.cs
    WorkspaceState.cs              # AppState 唯一所有者 + 节流持久化 + StateChanged
    WidgetRuntime.cs
    WidgetRuntimeHost.cs           # 实例集合 + 生命周期 + tick
    WidgetSurfaceFactory.cs        # 造/配 WidgetWindow（无数据所有权）
    PlacementController.cs         # 吸附 + placement 写回（无数据所有权）
    AppearanceController.cs        # theme/material 作用于运行中的表面（语言经 ShellEnvironment 委托，不引用 LanguageService）
    TickService.cs, ScreenProbe.cs, ScreenBoundsMapper.cs, ThemeService.cs
    Views/WidgetWindow.axaml(.cs), Views/WidgetGlowLayer.cs
  DeskKit.Persistence/             # 只依赖 Core
    StateStore.cs                  # : IStateStore
    Data/{DeskKitDbContext, DeskKitDbContextFactory, DatabaseOptions, SqliteConnectionPragmas,
          SettingsEntity, WidgetEntity, MetaEntity, Migrations/*}
    DatabaseBackup.cs, LegacyJsonImport.cs, AppPaths.cs
  DeskKit.Platform/                # 只依赖 Core
    {IDesktopLayerService 的实现不再在此；契约已移 Core}
    DesktopLayerPolicy.cs, MaterialPolicy.cs, ScreenBoundsMapper 已移 Runtime
    INotificationWindowStyler.cs(+Null), IAutoStartService.cs(+Null)
    Windows/{WindowsDesktopLayerService, WindowsWindowMaterialService, WindowsAutoStartService,
             WindowsShellIconLoader, WindowsNotificationWindowStyler, NotificationWindowStyling, DesktopDiagnostics}
    Interop/{Win32, NativeMethods}
    Null{DesktopLayerService,WindowMaterialService}
    PlatformServiceCollectionExtensions.cs
  DeskKit.Widgets/                 # 只依赖 Core
    Clock/, StickyNote/, QuickLaunch/, Localization/, Shared/, BuiltInWidgets.cs
  DeskKit.App/                     # Product
    App.axaml(.cs), Program.cs, SingleInstanceGuard.cs
    Shell/{WidgetShellComposition?, DefaultLayout.cs, TrayIconController.cs, SettingsWindowController.cs,
           WidgetCatalog.cs, ShellAssets.cs}
    Views/{SettingsWindow,NoticeWindow,Detail…}, ViewModels/*, Services/{LanguageService,NoticePresenter}   # 二者均为 Product 内部；Runtime 不引用
    Localization/AppLanguage.cs, Resources/, Assets/, Diagnostics/*
tests/
  DeskKit.Core.Tests/ DeskKit.Runtime.Tests/ DeskKit.Persistence.Tests/ DeskKit.Platform.Tests/ DeskKit.App.Tests/
```

**说明**：

- `WidgetWindow` 与 `WidgetGlowLayer` 迁入 **Runtime**（它们是“运行 widget 必需之物”，不是产品界面）。
- `ThemeService` 归 Runtime（widget 表面需要它）；它同时设置 app theme variant 属产品副作用，**保留现状**（O-4 不拆）。
- `WidgetText`/`WidgetLanguage` **留 Widgets**；`WidgetCatalog`（本地化名投影）归 **App**。
- 桌面层/材质的**契约**移 Core，Windows/Null **实现**留 Platform。

### 14.4 `DeskKit.Core` 的组成（定位的精确表述）

Core = **稳定契约 + 共享 Runtime 原语 + 少量外围能力端口**，三类具体为：

| 类别 | 内容 |
| --- | --- |
| **稳定契约**（多实现或跨层替换点） | `IWidgetProvider`、`IWidgetHost`、`IWidgetMessageBus`/`IWidgetMessage`、`IShellIconLoader`、`IStateStore`、`INoticePresenter`、`IDesktopLayerService`、`IWindowMaterialService` |
| **共享 Runtime 原语**（被 Runtime/Widgets/Product 共同使用的值类型与纯逻辑） | `WidgetDescriptor`、`WidgetContext`、`WidgetViewModel`（基类）、`WidgetSettings`、`WidgetPlacement`、`AppState`、`AppSettings`、`ScreenBounds`、`WidgetMaterial`、`WidgetSnapEngine`、`WidgetDragSession`、`WidgetResizeSession`、`WidgetGlowSegment`、`PlacementNormalizer`、`WidgetRegistry`、`SettingsMigrations`、`ThemeSetting`、`LanguageSetting`、`StoreOutcome` 家族 |
| **少量外围能力端口** | `INotificationWindowStyler` 之外的少量端口**不**放这里；`IO`/路径/发行为**不**属 Core。仅保留 `IStateStore`、`INoticePresenter` 这两个真正被 Runtime 消费、且需要测试替身的端口 |

**Core 不是**：

- 不是“第三方 widget 编译所需的最小集合”——那是早期表述，已废止。Widgets 恰好可以只依赖 Core（这是**结果**，不是目的），但 Core 同样要服务 Runtime 与 Persistence/Platform。
- 不是“纯领域层”：`WidgetViewModel` 依赖 Avalonia 与 CommunityToolkit.Mvvm（D-1），这是有意的。
- 不是能力的实现处：任何 EF、Win32、Lingua、托盘、设置窗口语义都不得进入。

> 因此 Core 的存在理由是**依赖面的稳定与共享**，而不是“为扩展性而存在”。

---

## 15. 最终依赖关系

### 15.1 论证：为什么必须建立 Runtime 边界（可否证伪）

1. **现状事实**：Runtime 相关类型（≈2126 行）与 Product 相关类型（≈2887 行）同居 `DeskKit.App`，且 `WidgetShell` 同时引用两者。
2. **膨胀机制**：只要 Runtime 协调器与产品 UI 同程序集同命名空间根，下一个 Runtime 需求（崩溃隔离、线程模型、插件解析、恢复策略、多表面类型）就会继续加进同一个类——这正是 947 行 God Object 的成因。
3. **接口是切点不是边界**：`IStateStore`/`IShellFacade` 让“将来可拆”成立，但不阻止拆分前膨胀。
4. **窗口属 Runtime**：`WidgetWindow` 是运行 widget 必需之物；因此 Runtime 直接拥有它，**无需** `IWidgetSurface`（这也是否决 `Application`/`UI` 拆分的原因）。
5. **可证伪**：若 Runtime 建成后仍引用 `Widgets`/`Persistence`/`App`，该边界无效 → 列为 D-3 并由 CI 检查。

### 15.2 项目依赖（D-2 / D-3）

```
                     DeskKit.Core
                  ┌───────┼───────┬────────┐
                  │       │       │        │
            DeskKit.Runtime  DeskKit.Persistence  DeskKit.Platform
                  │       │       │        │
                  └───────┴───┬───┴────────┘
                              │
                        DeskKit.Widgets
                              │
                         DeskKit.App
```

严格形式：

```
Runtime     ──→ Core
Persistence ──→ Core
Platform    ──→ Core
Widgets     ──→ Core
App         ──→ Core, Runtime, Persistence, Platform, Widgets
tests/*     →  各自被测项目（+ Core）
```

**禁止边（必须全部成立；CI 检查）**

```
Runtime     -X-> App / Widgets / Persistence
Persistence -X-> Runtime / Platform / Widgets / App
Platform    -X-> Runtime / Persistence / Widgets / App
Widgets     -X-> Runtime / Platform / Persistence / App
Core        -X-> 任何 DeskKit 项目，且 -X-> EF Core
```

**可达性结论（本轮逐类型验证）**：**全部可达**，无需例外。实现手段：

| 禁止边 | 实现手段 |
| --- | --- |
| Runtime -X-> Widgets | 文本解析**完全交给 Product**。**O-1 已否决**：不注入文本委托；`WidgetWindow.Title` 使用**稳定内部值**（`descriptor.Id`），不本地化（该标题在当前配置下不可见、无人读取） |
| Runtime -X-> Persistence | 只依赖 `IStateStore`（Core） |
| Runtime -X-> App | ①产品资源（应用图标 `WindowIcon?`）由 App 在构造 `WidgetSurfaceFactory` 时注入；②**语言变更经 `ShellEnvironment.ApplyLanguage` 委托注入**，Runtime **不得**引用 `LanguageService`（或任何 Lingua/Widgets 类型） |
| Platform -X-> Runtime | 平台契约已在 Core；Null 实现与策略不引用 Runtime |

### 15.3 运行期对象依赖

```
WidgetShell (facade: IWidgetHost + IShellFacade)
 ├── WorkspaceState         ← IStateStore, WidgetSettingsMigrator, WidgetSeedPolicy, AutoStartReconciliation
 ├── WidgetRuntimeHost      ← WidgetRegistry, WidgetSurfaceFactory, TickService, WorkspaceState
 ├── WidgetSurfaceFactory   ← IDesktopLayerService, IWindowMaterialService, WidgetMaterial, WidgetWindow,
 │                            WindowIcon?(App 注入)
 ├── PlacementController    ← WidgetSnapEngine, WorkspaceState, ICardRectSource(由 WidgetRuntimeHost 实现)
 └── AppearanceController   ← ThemeService, IWindowMaterialService（语言经 ShellEnvironment.ApplyLanguage 委托；**不注入 LanguageService**）

Product (App)
 ├── DefaultLayout          → WidgetSeedPolicy（唯一知道 "clock" 的地方）
 ├── WidgetCatalog          ← WidgetRegistry + WidgetText
 ├── TrayIconController     ← IShellFacade, WidgetCatalog, AppLanguage
 ├── SettingsWindowController ← IShellFacade, SettingsViewModel
 ├── NoticePresenter        ← INoticePresenter（App 实现）
 └── Composition Root       → StateStore(IStateStore), AddDeskKitPlatform(), AddBuiltInWidgets(), Serilog
```

### 15.4 端口清单（最终）

| 端口 | 定义 | 实现 | 消费者 | 删掉会失去什么 |
| --- | --- | --- | --- | --- |
| `IStateStore` | Core | Persistence | `WorkspaceState`；测试 fake | 脱库测试能力 + ORM 隔离 |
| `IShellFacade` | Runtime | `WidgetShell` | `SettingsViewModel`、`TrayIconController` | Product UI 可脱离 Runtime 具体类型测试的能力 |
| `INoticePresenter` | Core | App `NoticePresenter` | Runtime（`WidgetShell`/`WorkspaceState` 的存储告警）+ Product | **Runtime 使用的 Product capability port**（保持此边界，不再拆分）；删掉会失去无头运行（自检）能力 |
| `IDesktopLayerService` | Core | Platform（Windows/Null） | Runtime | 运行时能力端口 + OS 选择隔离 |
| `IDesktopGestureService` | Core | Platform（Windows/Null） | App（`DesktopGestureController`） | 桌面手势与 Win32 隔离；命令仍复用 `IShellFacade.SetWidgetsVisible`（R-8 未破） |
| `IWindowMaterialService` | Core | Platform（Windows/Null） | Runtime | 同上 |
| `IShellIconLoader` | Core | Platform（Windows/Null） | Widgets | widget 获取图标而不拖入 Win32 |
| `INotificationWindowStyler` | Platform | Platform.Windows | App `NoticeWindow` | 消除 UI→Windows 泄漏 |
| `IAutoStartService` | Platform | Platform（Windows/Null） | App（产品策略） | 产品能力隔离 |
| `IWidgetMessageBus` | Core | Core `WidgetMessageBus`（App 注册） | widget 之间 | widget 互不引用 |

**新增端口数：0。**（相对第二轮：**删除** `IWidgetTextResolver`。）

---

## 16. 对象模型参考表（最终）

| 类型 | 是什么 | 拥有者 | 创建者 | 销毁者 | 层 | 程序集（目标） |
| --- | --- | --- | --- | --- | --- | --- |
| `WidgetDescriptor` | widget **类型**静态描述（id、尺寸、`PreventActivation`、`SingleInstance`、显示名**资源键**） | Provider | Provider 静态构造 | — | Framework | Core |
| `IWidgetProvider` | 类型级工厂 + 描述（type-level 单例） | DI 容器 | `AddBuiltInWidgets()` / 插件注册 | 容器释放 | Framework | Core（契约）/ Widgets（实现） |
| `WidgetContext` | 实例创建时的只读上下文（**初始 placement 快照**、settings、host、instanceId） | `WidgetViewModel` | `WidgetRuntimeHost`（经 provider） | 随 VM | Framework | Core |
| `WidgetViewModel` | widget 内容 + 自持设置迁移；交出 `Control` | Widget（实现）/ 实例生命周期（宿主） | `IWidgetProvider.Create(context)` | `WidgetRuntimeHost.Destroy` | Framework | Core（基类）/ Widgets（实现） |
| `WidgetSettings` | 不透明配置簿（`Get/Set` + 变更通知） | Widget | `WidgetRuntimeHost`（由 `Placement.Settings` 构造） | 随 VM | Framework | Core |
| `WidgetPlacement` | **持久化实例记录**：身份 + 几何 + `SettingsVersion` + 不透明设置 | Runtime | `WorkspaceState` / `WidgetSeedPolicy` | `WorkspaceState`（用户移除） | Framework（记录）/ Runtime（实例） | Core |
| `WidgetRegistry` | provider 有序目录，拒绝重复 id | Runtime（DI 注入） | 组合根 | 容器释放 | Framework | Core |
| `WidgetRuntime` | `(Placement, ViewModel, Window, IsVisible)`，`internal` | `WidgetRuntimeHost` | `WidgetRuntimeHost` | `WidgetRuntimeHost` | Runtime | Runtime |
| `WidgetRuntimeHost` | **实例集合** + 生命周期 + tick 订阅/退订 | `WidgetShell` | 组合根 | 容器释放 | Runtime | Runtime |
| `WidgetSurfaceFactory` | 构造/配置 `WidgetWindow`（尺寸、material、图标、标题、事件接线）；**无数据所有权** | 无（无状态服务） | 组合根 | — | Runtime | Runtime |
| `WidgetWindow` | 无边框透明顶层窗口 + 卡片 + 拖拽/缩放 + 发光宿主 | `WidgetRuntime` | `WidgetSurfaceFactory` | `WidgetRuntimeHost.Destroy` | Runtime | Runtime |
| `WidgetGlowLayer` | 自绘发光控件（吸附反馈） | `WidgetWindow`（XAML 子元素） | XAML | 随窗口 | Runtime | Runtime |
| `WorkspaceState` | **`AppState` 唯一所有者** + 节流持久化 + `StateChanged` | `WidgetShell` | 组合根 | 容器释放 | Runtime | Runtime |
| `PlacementController` | 吸附计算 + 拖动/缩放后的 placement 写回；**无数据所有权** | 无 | 组合根 | — | Runtime | Runtime |
| `AppearanceController` | theme/material 作用于运行中的 widget 表面；语言**只**经 `ShellEnvironment.ApplyLanguage` 委托；**不依赖 `LanguageService`**；**无数据所有权** | 无 | 组合根 | — | Runtime | Runtime |
| `TickService` | 单一 `DispatcherTimer` 驱动全部 `ITickAware` | `WidgetRuntimeHost` | 组合根 | 容器释放 | Runtime | Runtime |
| `ScreenProbe` | 离屏 1×1 窗口取得 `Screens` → `ScreenBounds` | 无（静态缓存） | 首次调用 | — | Runtime | Runtime |
| `ThemeService` | 主题变体应用 + 卡片画刷解析 | 无（无状态） | 组合根 | — | Runtime | Runtime |
| `WidgetShell` | Runtime 门面：`IWidgetHost` + `IShellFacade` + 启动顺序 | App | 组合根 | App 退出 | Runtime（facade） | Runtime |
| `IShellFacade` | Product UI ↔ Runtime 契约 | 无 | — | — | Runtime（契约） | Runtime |
| `DefaultLayout` | **产品数据**：首启创建哪些 widget | App | App | — | Product | App |
| `TrayIconController` | 托盘图标与原生菜单（语言刷新、显示/隐藏、退出） | App | App | App 退出 | Product | App |
| `SettingsWindowController` | 设置窗口单例生命周期 | App | App | App 退出 | Product | App |
| `NoticePresenter` | 一次性告警窗口（`INoticePresenter` 实现） | App | App | 自关闭定时器 | Product | App |
| `WidgetCatalog` | `WidgetRegistry` → **带本地化名**的列表 | App | App | App 退出 | Product | App |
| `SettingsViewModel`/`SettingsWindow` | 设置 UI | App | `SettingsWindowController` | 窗口关闭 | Product | App |
| `SingleInstanceGuard` / Serilog / `AppLanguage` | 产品机制 | App | App | App 退出 | Product | App |
| `DesktopLayerSelfTest` | 真实桌面行为验证（随产品发布） | App | App（`--selftest`） | 进程退出 | Product | App |
| `StateStore` | `IStateStore` 适配器（EF/SQLite） | DI 容器 | 组合根 | 容器释放 | 支撑 | Persistence |
| `DeskKitDbContext` / Entities / Migrations | 存储实现细节 | Persistence | EF | 容器释放 | 支撑 | Persistence |

### 16.1 状态三分类（必须区分）

1. **持久化 widget 配置**：`WidgetPlacement.Settings` + `SettingsVersion`；**widget 拥有**；宿主不解读。
2. **瞬时派生状态**：VM 字段（`ClockViewModel.TimeText`）；**不入库**。
3. **宿主运行态**：`WidgetRuntime.IsVisible`、窗口实时 Position/Size；**宿主拥有**；仅在用户拖动/缩放后写回 `WidgetPlacement`。

### 16.2 身份（D-…）

- **type identity**：`WidgetDescriptor.Id`（== `WidgetPlacement.WidgetId`）；provider 拥有。
- **instance identity**：`WidgetPlacement.InstanceId`；宿主生成（GUID "N"）；DB 主键；跨会话稳定、跨机器不保证。
- 第三方 type id 命名规则（厂商前缀）**只登记，不实现**（O-6 / N-1）。

### 16.3 `WidgetContext.Placement` 语义（D-5）

**是 widget 创建时的初始 placement 快照，不保证代表当前实时 placement。**

- 保留现有名称（**不改名**）。
- 唯一改动：`DeskKit.Core/Models/WidgetViewModel.cs` 中为 `WidgetContext.Placement` 增加 XML 文档说明。
- 依据：`CapturePlacement` 替换的是 `WidgetRuntime.Placement` 与 `AppState` 记录；全仓库检索确认**无任何 widget 读取 `Context.Placement`**（仅 `IWidgetHost.Screens` 被间接使用）。
- 若未来出现“需要实时 placement”的真实需求，再评估查询式 API；**现在不做**。

---

## 17. 最终 Widget 生命周期

```
[Catalogued]   provider 已注册，无实例
      │ AddWidget / DefaultLayout(首启) / 启动恢复
      ▼
[Placed]       WidgetPlacement 进入 WorkspaceState（未建窗）
      │ provider.Create(context)     ── 异常 → 回 [Catalogued]，不写入 State
      ▼
[Constructed]  WidgetViewModel 已建
      │ WidgetSurfaceFactory.Create(...)   （material、尺寸、图标、标题、事件接线、tick 订阅）
      ▼
[Surfaced]     WidgetWindow 已建，未显示
      │ window.Show()
      ▼
[Shown]        窗口已显示
      │ viewModel.Start()            ── 异常 → 销毁，回 [Catalogued]
      ▼
[Running]      Start 已返回；可收 tick / 消息
      │ SetVisible(false) ⇄ true     （平台可见性；不触发 Stop，不改变 [Placed]/[Running]）
      │ 拖动/缩放 → PlacementController 写回 placement（仍 [Running]）
      ▼
[Stopping]     Stop() → Dispose()
      ▼
[Disposed]     从实例集合移除 → tick 退订 → window.Close()
```

**顺序规则（必须保持；现由自检覆盖）**

1. `Show()` 之后才 `Start()`。
2. `Stop()` + `Dispose()` 之后才 `Close()`。
3. 任一步异常不得留下半初始化实例（保留 `try/catch → Destroy` 语义）。
4. 隐藏/显示是**平台可见性**，不得触发创建/销毁/`Stop()`。
5. 拖动/缩放只写回 placement，不重建窗口。

**归属**：转移决策 → `WidgetRuntimeHost`；表面构造/销毁 → `WidgetSurfaceFactory`；钩子实现 → `WidgetViewModel`；placement 持久化 → `WorkspaceState`。**生命周期不属于**：托盘/设置/告警（Product 各自管理）；持久化文件（`IStateStore`）。

**时序修正（O-2，已采用，P3 实施）**：`ITickAware` 目前在 `window.Show()` **之前**订阅（理论上可能在 `Start()` 前收到一次 tick）；改为在 `Start()` 成功、进入 `[Running]` 之后订阅。另在契约注释写明“`Stop()` 可能在 `Start()` 未成功时被调用，必须幂等”。

---

## 18. 决策登记

### 18.1 DECISION（必须执行）

| ID | 决策 |
| --- | --- |
| **D-1** | Widget 契约保持 Avalonia + MVVM 形态：`CreateView()`/`CreateSettingsView()` 返回 `Control`；不引入非 Avalonia 抽象。 |
| **D-2** | 六个项目：`Core, Runtime, Persistence, Platform, Widgets, App`。保留 `Core` 名称（不做无收益 rename）。 |
| **D-3** | 项目依赖只允许第 15.2 节形式；**Runtime 只依赖 Core**；Core 不依赖任何 DeskKit 项目与 EF Core。**必须由 CI 检查**（`dotnet list reference`/脚本断言）。 |
| **D-4** | 首启默认布局属 **Product**（`DeskKit.App/Shell/DefaultLayout.cs`）；`WidgetDescriptor` **不增**播种字段；Runtime/Widgets 不知道默认 widget。 |
| **D-5** | `WidgetContext.Placement` = **创建时初始快照**；保留名称，只加 XML 说明。 |
| **D-6** | 平台归属按消费者：Runtime 消费的能力端口（`IDesktopLayerService`、`IWindowMaterialService`、`WidgetMaterial`、`WidgetMaterial.FillsWindow`）进 **Core**；`MaterialPolicy.Resolve`/`DesktopLayerPolicy`/实现留 **Platform**；`IShellIconLoader` 留 Core；`IAutoStartService`/`INotificationWindowStyler` 留 Platform。 |
| **D-7** | Runtime **不解析面向用户文本**；`WidgetCatalog` 归 App；**不引入** `IWidgetTextResolver`。**O-1 已否决**：`ShellEnvironment` **不含**文本委托；`WidgetWindow.Title` 使用**稳定内部值**（`descriptor.Id`），不做本地化（该标题不可见、且全仓库无人读取）。 |
| **D-8** | `WorkspaceState` 只拥有 `AppState` + 节流持久化 + `StateChanged`；**不得**含 migration/autostart/window/UI 职责。 |
| **D-9** | `WidgetRuntimeHost`（实例+生命周期+tick）／`WidgetSurfaceFactory`（表面构造）／`PlacementController`（吸附+写回）／`AppearanceController`（**仅** theme/material 作用于运行中的表面）／`WidgetVisibilityAnimator`（**仅**显示/隐藏时的滑动，纯规则在 Core 的 `WidgetSlideAnimation`）四分，互相不承担对方职责。**`AppearanceController` 与 `WidgetShell` 均不得引用 `LanguageService`**：语言变更只经 `ShellEnvironment.ApplyLanguage` 委托传入（**唯一**注入到 Runtime 的委托；文本委托已按 O-1 否决）。 |
| **D-10** | Tray / SettingsWindow / Notice 属 **Product**；`SettingsWindow` 不得出现在 Runtime。`INoticePresenter` 的**契约**在 Core，作为 **Runtime 使用的 Product capability port** 保留现状，**不再为“纯洁性”继续拆分**（由 App 提供实现：`NoticePresenter`）。 |
| **D-11** | `IStateStore` + `StoreOutcome`/`LoadReport`/`SaveReport`/`StoreProblem` + `AppState`/`AppSettings`/`WidgetPlacement`/`WidgetSettings` 属 **Core**；`StateStore`/`DbContext`/`Entities`/`Migrations`/`DatabaseBackup`/`LegacyJsonImport`/`AppPaths` 属 **Persistence**。 |
| **D-12** | `WidgetPlacement` 是 Runtime 领域记录，**不是** EF Entity；映射（`WidgetPlacement ↕ WidgetEntity`）只发生在 Persistence；Core/Runtime 不得引用数据库实体。 |
| **D-13** | `DesktopLayerSelfTest` 保留在 App 并随产品发布；诊断层允许引用 `DeskKit.Platform.Windows`。 |
| **D-14** | 不为窗口/管理器/目录造接口：`IWidgetWindow`、`IWidgetSurface`、`IWidgetManager`、`IWidgetRuntimeManager`、`IWidgetCatalog` 一律不加。`IShellFacade` 例外（有真实跨层消费者）。 |

### 18.2 OPTIONAL（**已定稿**）

| ID | 项 | 裁定 |
| --- | --- | --- |
| O-1 | `WidgetSurfaceFactory` 注入 `Func<string,string>?` 以保留（不可见的）本地化窗口标题 | **不采用**。不引入文本委托；`WidgetWindow.Title` 使用稳定内部值（`descriptor.Id`）；`ShellEnvironment` 中删除 `ResolveWidgetText` |
| O-2 | `ITickAware` 订阅从 “show 之前” 改到 “Start 之后”（进入 `[Running]` 后） | **采用**，在 **P3** 实施（由 `WidgetRuntimeHost` 承担），并新增“Start 前不派发 tick”的测试 |
| O-3 | `AppPaths` 拆为 `StoragePaths`(Persistence) + `LogPaths`(App) | **不采用**：保留 `AppPaths` 于 Persistence，App 读取其日志路径 |
| O-4 | `ThemeService` 拆出 `IAppTheme` 端口 | **不采用**：`ThemeService` 留在 Runtime，维持现状 |
| O-5 | `MaterialPolicy.FillsWindow` 的测试随方法移入 Core.Tests | **采用**（按当前 plan.md 执行） |
| O-6 | 第三方 widget type id 命名规则（厂商前缀） | **只登记**为文档待办，不实现（按当前 plan.md 执行） |

> 另：`INoticePresenter` **保持当前边界**，不进一步拆分；它在代码与文档中明确为 **Runtime 使用的 Product capability port**（契约在 Core，实现 `NoticePresenter` 在 App）——见 D-10。

### 18.3 DO NOT DO YET

| ID | 项 | 原因 |
| --- | --- | --- |
| N-1 | DLL plugin loader / `AssemblyLoadContext` | 版本、隔离、信任策略未定义 |
| N-2 | MediatR / CQRS / 通用 EventBus | `IWidgetMessageBus` 已够；无跨进程/管道需求 |
| N-3 | 拆 `Platform.Windows` 项目 | 只有一套实现 |
| N-4 | 新建 `Application` / `UI` 项目 | `CreateView(): Control` 使拆分成为形式（第 15.1 节） |
| N-5 | 重写 UI / 全面 MVVM 化 | 无收益，风险高 |
| N-6 | 打开 trimming / AOT | `WidgetSettings.Get/Set` 为反射式开放泛型 |
| N-7 | Generic Repository / Unit of Work | `IStateStore` 已是正确粒度 |
| N-8 | 把 `DesktopLayerSelfTest` 移出发布产物 | 产品行为（真实桌面验证随应用发布） |
| N-9 | 改动像素单位约定（位置物理/尺寸逻辑） | 防高 DPI 漂移的关键 |
| N-10 | `WidgetGlowLayer` 大规模封装改造 | 可调成员可能是真实 API |

---

## 19. 类型 / 文件迁移映射（完整）

### 19.1 `Core` → `Persistence`（P2）

| 现文件 | 目标文件 | 职责迁移 |
| --- | --- | --- |
| `Core/Data/DeskKitDbContext.cs` | `Persistence/Data/DeskKitDbContext.cs` | EF 模型配置 → 原样（命名空间 `DeskKit.Persistence.Data`） |
| `Core/Data/DeskKitDbContextFactory.cs` | `Persistence/Data/DeskKitDbContextFactory.cs` | 设计时工厂 → 原样 |
| `Core/Data/DatabaseOptions.cs` | `Persistence/Data/DatabaseOptions.cs` | 连接选项 → 原样 |
| `Core/Data/SqliteConnectionPragmas.cs` | `Persistence/Data/SqliteConnectionPragmas.cs` | WAL/synchronous/busy_timeout 拦截器 → 原样（`internal`） |
| `Core/Data/SettingsEntity.cs` | `Persistence/Data/SettingsEntity.cs` | DB 实体 → 原样 |
| `Core/Data/WidgetEntity.cs` | `Persistence/Data/WidgetEntity.cs` | DB 实体 → 原样 |
| `Core/Data/MetaEntity.cs` | `Persistence/Data/MetaEntity.cs` | DB 实体 → 原样 |
| `Core/Data/Migrations/20261002062527_InitialCreate.cs(+.Designer.cs)` | `Persistence/Data/Migrations/…` | 迁移 → 原样（**MigrationId 不变**） |
| `Core/Data/Migrations/DeskKitDbContextModelSnapshot.cs` | `Persistence/Data/Migrations/…` | 快照 → 命名空间调整（若需，重新生成） |
| `Core/Services/StateStore.cs` | `Persistence/StateStore.cs` | 加载/保存/迁移/备份/导入编排 → **实现 `IStateStore`** |
| `Core/Services/DatabaseBackup.cs` | `Persistence/DatabaseBackup.cs` | SQLite backup API → 原样（`internal`） |
| `Core/Services/LegacyJsonImport.cs` | `Persistence/LegacyJsonImport.cs` | 旧 JSON 只读导入 → 原样（`internal`） |
| `Core/AppPaths.cs` | `Persistence/AppPaths.cs` | 路径常量 → 原样 |
| `Core/Services/StoreOutcome.cs` | **留 Core**（`Core/Abstractions/StoreOutcome.cs`） | `StoreOutcome`/`StoreProblem`/`StoreLoadReport`/`StoreSaveOutcome`/`StoreSaveReport` 是端口契约 |
| `tests/Core.Tests/StateStoreTests.cs` | `tests/Persistence.Tests/StateStoreTests.cs` | 真实库测试 → 原样迁移 |
| `Core.csproj` 的 `InternalsVisibleTo` | 移到 `Persistence.csproj`（指向 `DeskKit.Persistence.Tests`） | — |

### 19.2 新增（Core）

| 新文件 | 内容 |
| --- | --- |
| `Core/Abstractions/IStateStore.cs` | `DatabasePath`、`PreMigrationBackupPath`、`LoadReport`、`HasStoredState`、`Load()`、`Save(AppState)` |
| `Core/Abstractions/IDesktopLayerService.cs` | 契约 + `DesktopLayerOptions`（自 Platform 迁入） |
| `Core/Abstractions/IWindowMaterialService.cs` | 契约（自 Platform 迁入） |
| `Core/Abstractions/WidgetMaterial.cs` | 枚举（自 Platform 迁入）+ `FillsWindow` 扩展（自 `MaterialPolicy` 拆出） |
| `Core/Abstractions/INoticePresenter.cs` | `Notice` 记录 + `INoticePresenter` + `NullNoticePresenter`（自 App 迁入，端口化） |
| `Core/Services/WidgetSettingsMigrator.cs` | 自 `WidgetShell.MigrateWidgetSettings` 抽出的纯逻辑 |
| `Core/Services/WidgetSeedPolicy.cs` | 给定默认布局条目 + `ScreenBounds` → `WidgetPlacement` 列表 |
| `Core/Models/AutoStartReconciliation.cs` | 自 `WidgetShell.ReconcileAutoStart` 抽出的纯规则 |
| `Core/Models/WindowCardGeometry.cs` | 自 `WidgetWindow.WindowSizeForCard`/`CardSizeForWindow` 抽出的纯算术 |

### 19.3 新增 / 迁移（Runtime）

| 现文件 | 目标 | 职责迁移 |
| --- | --- | --- |
| `App/Services/WidgetShell.cs` | `Runtime/WidgetShell.cs`（**facade**，目标 <250 行） | 只保留：启动顺序编排、`IWidgetHost`、`IShellFacade`、`Dispose` |
| ↑ 的 `_stateStore`/`State`/`StateChanged`/`ScheduleSave`/`SaveNow` | `Runtime/WorkspaceState.cs` | `AppState` 唯一所有者 + 500ms 节流 + 拒绝写入 |
| ↑ 的 `MigrateWidgetSettings`（循环） | `Core/Services/WidgetSettingsMigrator.cs` | 纯逻辑下沉 |
| ↑ 的 `SeedDefaultWidgets`/`CreatePlacement`（默认位置） | `Core/Services/WidgetSeedPolicy.cs` + App 的 `DefaultLayout` | 规则下沉；**默认内容属 Product** |
| ↑ 的 `ReconcileAutoStart` | `Core/Models/AutoStartReconciliation.cs` | 纯规则下沉 |
| ↑ 的 `_widgets`/`AddWidget`/`CreateWidget`/`DestroyWidget`/`Remove`/`FindRuntime`/`WindowSizeForPlacement`/tick 订阅 | `Runtime/WidgetRuntimeHost.cs` | 实例集合 + 生命周期 + tick |
| ↑ 的窗口构造（`new WidgetWindow{…}`、尺寸/材质/图标/标题/上下文菜单接线/事件接线） | `Runtime/WidgetSurfaceFactory.cs` | 表面构造；**无数据所有权**。`Title` 改用**稳定内部值**（`descriptor.Id`），不再走 `WidgetText`（O-1 已否决） |
| ↑ 的 `CapturePlacement`/`SnapPosition`/`ClearSnapHighlights`/`OnDragCompleted`/`WindowScaling`/`TryGetCardSize`/`TryGetCardRect`/`SnapGap`/`SnapThreshold` | `Runtime/PlacementController.cs` | 吸附 + 写回；**不含生命周期** |
| ↑ 的 `ApplySettings` 的 theme/material 部分、`foreach widget.Window.CardBackground = …` | `Runtime/AppearanceController.cs` | 运行时外观作用（**仅 theme/material**）；**不依赖 `LanguageService`**、不处理自启动 |
| ↑ 的 `ApplySettings` 的 autostart 部分 | App（Product 策略）→ 调 Core 的 `AutoStartReconciliation` + `IAutoStartService` | 产品能力 |
| ↑ 的 `Widgets`/`AvailableWidgets` 投影中的 `WidgetText.Value` | App `Shell/WidgetCatalog.cs` | 文本解析归 Product |
| ↑ 的 `_trayIcon`/`CreateTrayIcon`/`BuildContextMenu` | App `Shell/TrayIconController.cs` | Product UI |
| ↑ 的 `_settingsWindow`/`OpenSettings`/`CreateSettingsWindow` | App `Shell/SettingsWindowController.cs` | Product UI |
| ↑ 的 `_appIcon`/`GetAppIcon`（`avares://DeskKit.App/Assets/deskkit.ico`） | App `Shell/ShellAssets.cs` → 作为 `ShellEnvironment.Icon` 注入 Runtime | 产品资源 |
| ↑ 的 `ShowStorageNotice`（`AppLanguage` + `INoticePresenter`） | App（Product）读 `IShellFacade.LoadReport` 后展示 | 产品通知 |
| ↑ 的 `Language.Apply(...)` 调用 | 委托 `ShellEnvironment.ApplyLanguage` 注入 | Runtime 不引用 Lingua/Widgets |
| `App/Services/WidgetRuntime.cs` | `Runtime/WidgetRuntime.cs` | 原样（`WidgetInfo` 记录保留，供 Product 使用） |
| `App/Services/TickService.cs` | `Runtime/TickService.cs` | 原样 |
| `App/Services/ScreenProbe.cs` | `Runtime/ScreenProbe.cs` | 原样 |
| `App/Services/ThemeService.cs` | `Runtime/ThemeService.cs` | 原样（`CardBrushFor` 改用 Core 的 `WidgetMaterial.FillsWindow`） |
| `App/Views/WidgetWindow.axaml(.cs)` | `Runtime/Views/WidgetWindow.axaml(.cs)` | 原样 + 约 25 个自检成员降 `internal`（P1 已完成） |
| `App/Views/WidgetGlowLayer.cs` | `Runtime/Views/WidgetGlowLayer.cs` | 原样 |
| `App/Services/INoticePresenter.cs` | `Core/Abstractions/INoticePresenter.cs` | 端口化 |
| `App/Services/NoticePresenter.cs` | App（不变位置） | 实现 Core 端口（Product） |
| `App/Services/LanguageService.cs` | App（不变位置） | Product；`Apply` 经 `ShellEnvironment.ApplyLanguage` 交给 Runtime |
| `App/Localization/AppLanguage.cs` | App（不变） | Product |
| （新） | `Runtime/IShellFacade.cs` | Product UI ↔ Runtime 契约 |
| （新） | `Runtime/ShellEnvironment.cs` | 数据记录（**非接口**）：`WindowIcon? Icon`、`Action<string>? ApplyLanguage`。**不含文本委托**（O-1 已否决） |

### 19.4 新增 / 迁移（Platform）

| 现文件 | 目标 | 说明 |
| --- | --- | --- |
| `Platform/IDesktopLayerService.cs` | → `Core/Abstractions/`（契约） | `NullDesktopLayerService.cs` 留 Platform |
| `Platform/IWindowMaterialService.cs` | → `Core/Abstractions/`（契约） | `NullWindowMaterialService.cs` 留 Platform |
| `Platform/WidgetMaterial.cs` | → `Core/Abstractions/` | 枚举 |
| `Platform/MaterialPolicy.cs` | **拆分**：`FillsWindow` → `Core/Abstractions/WidgetMaterial.cs`（扩展/静态）；`Resolve` + build 常量留 Platform | D-6 |
| `Platform/ScreenBoundsMapper.cs` | → `Runtime/ScreenBoundsMapper.cs` | 仅 Runtime 消费 |
| `Platform/DesktopLayerPolicy.cs` | 留 Platform | Runtime 不消费 |
| `Platform/Windows/NotificationWindowStyling.cs` | `Platform/Windows/WindowsNotificationWindowStyler.cs`（实现新契约）+ 保留静态样式助手 | 修 P-6 |
| （新） | `Platform/INotificationWindowStyler.cs`（+`NullNotificationWindowStyler`） | 契约在 Platform 根 |
| `Platform/PlatformServiceCollectionExtensions.cs` | 增加 `INotificationWindowStyler` 注册 | 唯一选择点不变 |

### 19.5 App 内新增（Product）

| 新文件 | 内容 |
| --- | --- |
| `App/Shell/DefaultLayout.cs` | **产品数据**：首启创建哪些 widget（当前为 `clock` 一项，含尺寸） |
| `App/Shell/WidgetCatalog.cs` | `WidgetRegistry` + `WidgetText` → 带本地化名的列表（供设置窗口与托盘使用，含语言变更刷新） |
| `App/Shell/TrayIconController.cs` | 托盘图标 + 原生菜单（Add widget / 显示隐藏 / 设置 / 退出）+ 语言订阅 |
| `App/Shell/SettingsWindowController.cs` | 设置窗口单例生命周期（`SettingsViewModel(this facade, languageService)`；**两者都是 Product 内部依赖**，Runtime 不参与） |
| `App/Shell/ShellAssets.cs` | 进程级共享的 `WindowIcon`（`avares://DeskKit.App/Assets/deskkit.ico`） |

### 19.6 旧职责 → 新职责 对照（`WidgetShell` 关键项）

```
旧职责                                     新职责
─────────────────────────────────────────────────────────────────────────
State 所有权 + 节流写盘                →   WorkspaceState
Widget 生命周期 + tick 订阅            →   WidgetRuntimeHost
窗口创建/尺寸/材质/事件接线            →   WidgetSurfaceFactory
Snap + placement 写回                  →   PlacementController
theme/material 作用于运行中表面        →   AppearanceController
widget 名称本地化                      →   App/WidgetCatalog（Product）
首启默认 widget                        →   App/DefaultLayout（Product）
托盘 + 右键菜单                        →   App/TrayIconController（Product）
设置窗口                               →   App/SettingsWindowController（Product）
存储告警                               →   App（Product，读 IShellFacade.LoadReport）
语言应用                               →   App 注入的 ShellEnvironment.ApplyLanguage
自启动对齐                             →   Core 规则 + App 策略
设置迁移规则                           →   Core/WidgetSettingsMigrator
启动顺序编排 + IWidgetHost             →   WidgetShell（facade，保留）
```

---

## 20. 重构阶段（P0–P6）

统一验证（每阶段结束都执行）：`dotnet build DeskKit.slnx -c Release`（0 warning/0 error）→ `dotnet test DeskKit.slnx -c Release`（全绿）→ **在开发机上** `dotnet run --project src/DeskKit.App -- --selftest --out artifacts/selftest-report.txt`（退出码 0、0 FAIL）。
> 自检依赖真实桌面/GPU，**CI 不执行**；CI 只保证编译与单测。

---

### P0 — 锁定决策 + 记录真实 baseline

| 项 | 内容 |
| --- | --- |
| 修改文件 | 无（仅文档） |
| 新增文件 | 无（`docs/architecture.md` 的决策段落在 P5 统一更新） |
| 删除文件 | 无 |
| 类型移动 | 无 |
| API 变化 | 无 |
| 测试变化 | 无；只记录**基线数字** |
| 行为验证 | **首次实际执行** `dotnet build` / `dotnet test` 并记录通过数（Current Verification）；另行执行 `--selftest` 得到**本轮**报告（Historical Evidence：21 节 / 203 PASS / 0 FAIL，仅作对照） |
| 退出条件 | D-1..D-14 经评审确认；O-1..O-6 逐项定案；Current Verification 数字记录在案 |
| **Rollback** | 无需（未改代码） |

---

### P1 — 纯规则下沉 + `WidgetWindow` 封装收敛

| 项 | 内容 |
| --- | --- |
| 修改文件 | `src/DeskKit.App/Services/WidgetShell.cs`、`src/DeskKit.App/Views/WidgetWindow.axaml.cs` |
| 新增文件 | `Core/Services/WidgetSettingsMigrator.cs`、`Core/Services/WidgetSeedPolicy.cs`、`Core/Models/AutoStartReconciliation.cs`、`Core/Models/WindowCardGeometry.cs` |
| 删除文件 | 无（`WidgetShell` 中对应私有方法改为调用新类型） |
| 类型移动 | 无公共类型移动；4 个新类型加入 Core |
| API 变化 | 无公共 API 变化；`WidgetWindow` 约 25 个成员 `public → internal` |
| 测试变化 | 新增 `WidgetSettingsMigratorTests`、`AutoStartReconciliationTests`、`WindowCardGeometryTests`、`WidgetSeedPolicyTests`（`WidgetSeedPolicy` 输入用显式 id 列表，P4 换为产品 `DefaultLayout`） |
| 行为验证 | build/test + 自检（重点第 9/11/12/17/19 节） |
| 预期指标 | ~~`WidgetShell` 947 → **~800 行**~~ **估计有误，见下**；`WidgetWindow` public 51 → **≤30** |
| **实际结果**（2026-10-02 18:2x，commit 见下） | build **0 警告 / 0 错误**；test **222 通过 / 0 失败**（Core **181**，含新增 33；Platform 41）；selftest **PASS (203 checks)**、退出码 0、21 节、0 FAIL。`WidgetWindow` public **51 → 19**（internal 31）✅；`WidgetShell` 总行数 947 → 943、**代码行（去空行与注释）624 → 620** ❌ 未达 ~800 |
| 估计为何有误 | 947 行里只有 624 行是代码；被抽出的 4 组规则合计约 90 行，而调用点与说明使净减有限。`WidgetShell` 的**主体**（Snap、窗口构造、托盘、设置窗口、告警）在 **P3/P4** 才移出，故 P3 的 `<250 行` 才是真正的收敛指标 —— 本行数字不再作为 P1 的通过条件 |
| **Rollback** | 纯新增 + 委托调用，`git revert` 单个提交即可；无状态迁移、无数据格式变化 |

---

### P2 — 拆出 `DeskKit.Persistence`

| 项 | 内容 |
| --- | --- |
| 修改文件 | `DeskKit.slnx`、`Core/DeskKit.Core.csproj`（删 EF 两个包）、`App/DeskKit.App.csproj`（加引用）、`Core.Tests.csproj`、`App.axaml.cs`（`AddSingleton<IStateStore, StateStore>()`）、`WidgetShell`（参数类型 `StateStore` → `IStateStore`） |
| 新增文件 | `src/DeskKit.Persistence/DeskKit.Persistence.csproj`、`tests/DeskKit.Persistence.Tests/DeskKit.Persistence.Tests.csproj`、`Core/Abstractions/IStateStore.cs`、`Core/Abstractions/StoreOutcome.cs`（自 Services 移入） |
| 删除文件 | `Core/Data/**`（移走）、`Core/Services/StateStore.cs`、`DatabaseBackup.cs`、`LegacyJsonImport.cs`、`Core/AppPaths.cs`（移走）；`Core.Tests/StateStoreTests.cs`（移走） |
| 类型移动 | 见 19.1 |
| API 变化 | `StateStore`：`DeskKit.Core.Services` → `DeskKit.Persistence`；`AppPaths`：`DeskKit.Core` → `DeskKit.Persistence`；新增 `IStateStore`。**均无外部消费者**（v0.1.0） |
| 测试变化 | `StateStoreTests` 迁到 Persistence.Tests；**新增迁移兼容断言**：`GetMigrations()` 与 `GetAppliedMigrations()` 相等，且 `__EFMigrationsHistory.MigrationId == "20261002062527_InitialCreate"` |
| 行为验证 | build/test + 自检**第 16/17/18/20 节**（导入、设置迁移、降级存储、写中杀进程）；另用含旧库的机器确认不触发 `NewerSchema`/`Unavailable`/迁移备份 |
| 预期指标 | `dotnet list src\DeskKit.Widgets package --include-transitive` **不含** EF Core / SQLite |
| **Rollback** | 阶段可整体 `git revert`（文件移动 + 命名空间变更）；**若已发布**，因 MigrationId 未变，回退后旧构建仍可读同一数据库 → 风险低。先决：P2 不得同时改 EF 模型 |

---

### P3 — 在 `DeskKit.App` 内拆分 `WidgetShell`（行为不变）

| 项 | 内容 |
| --- | --- |
| 修改文件 | `App/Services/WidgetShell.cs`（逐步变薄）、`App.axaml.cs`（注册新协作者）、`App/ViewModels/SettingsViewModel.cs`（改用 `IShellFacade`）、`App/Services/ThemeService.cs`（改用 `WidgetMaterial.FillsWindow`）、`App/Diagnostics/*`（若引用发生位移） |
| 新增文件 | `App/Shell/{WorkspaceState,WidgetRuntimeHost,WidgetSurfaceFactory,PlacementController,AppearanceController,WidgetCatalog,TrayIconController,SettingsWindowController,DefaultLayout,ShellAssets,IShellFacade,ShellEnvironment}.cs` |
| 删除文件 | 无（原文件变薄，方法搬走） |
| 类型移动 | 本阶段仍在 App 内，仅命名空间 `DeskKit.App.Services` → `DeskKit.App.Shell`；`INoticePresenter` → Core |
| API 变化 | `SettingsViewModel` 依赖 `IShellFacade` 而非具体 `WidgetShell`；`WidgetShell` 公开面收缩为 `IWidgetHost` + `IShellFacade` + 启动/释放 |
| 测试变化 | 新增 `tests/DeskKit.App.Tests`：`WorkspaceStateTests`（fake `IStateStore`）、`PlacementControllerTests`（fake `ICardRectSource`）、`SettingsViewModelTests`（fake `IShellFacade`）、`WidgetCatalogTests`（资源键）；**新增 O-2 断言**：widget 在 `Start()` 返回前不收到 tick，之后才收到 |
| 行为验证 | build/test + 自检全绿（重点第 9/10/11/14/19 节） |
| 预期指标 | `WidgetShell.cs` < **250 行**；`SettingsViewModel` 无 `WidgetShell` 直接引用；Runtime 语义类型已不与托盘/设置同命名空间根；**O-2 已实施**（tick 在 `Start()` 后订阅） |
| **实际进度**（2026-10-02，P3 分三部分完成，共 10 个提交） | **part 1**（`c4a083f`）：`WorkspaceState`、`WidgetCatalog`、`DefaultLayout`、`ShellAssets`；`INoticePresenter` 迁至 Core；`WidgetWindow.Title` 改为稳定内部值（O-1 落定）。**part 2**（`71beefc`）：`PlacementController` + `IPlaceableWidget`，新增 `tests/DeskKit.App.Tests`（8 个测试，替身矩形验证吸附/发光/边距）。**part 3**（`40e909a`、`7f31b15`、`8db2eae`、`ada6a3b`、`dffda20`、`dd6c6c1`、`45527bf`、`9654490`）：`WidgetRuntimeHost`、`WidgetSurfaceFactory`、`AppearanceController`、`IShellFacade`+`SettingsWindowController`、`TrayIconController`+`WidgetContextMenuFactory`+`StorageNoticePresenter`、`ShellStartup`、`AutoStartController`、门面收口、O-2 自检。**P3 验收通过** |
| 当前指标 | `WidgetShell.cs` **947 → 245 行**（目标 <250，**已达成**；其中非空非注释 154 行）；`SettingsViewModel` **不引用 `WidgetShell`**（✅）；门面只保留启动顺序、`IWidgetHost`、`IShellFacade`、`Dispose`；O-2 已实施**并有能失败的自检断言**（见下） |
| 已验证 | 每个提交均：build **0 警告 / 0 错误**；测试 **256 通过 / 0 失败**（Core 159、Platform 41、Persistence 26、App 30）；自检 **PASS (209 checks)**、退出码 0、22 节、0 FAIL |
| part 3 的关键约束（已满足） | 每个提交都可编译、可测试、可单独回退；`WidgetContext` 需要 `IWidgetHost`，`WidgetRuntimeHost.Add` 因此接收宿主与 `Screens`；`Remove`（销毁 + 移除 placement + 通知）保留在门面，避免把状态所有权再拆散；`WidgetRuntimeHost` 另收 `Add(provider, …)`（按已放置数量派生 placement）与 `SetVisible`（托盘隐藏命令），门面因此不再持有 `IDesktopLayerService` |
| **P3 期间发现并修掉的缺陷** | `AppearanceController` 在构造时收到的是**尚未赋值**的 `_material` 字段（枚举默认值 `None`），因此主题切换时会把每张卡片重绘成不透明画刷，压在**填满窗口的材质**（本机为 `Mica`）之上 —— 由 P3-3.3 的抽取引入。修法不是调整赋值顺序，而是**让重绘向窗口本身询问材质**（`runtime.Window.Material`），从结构上消除“第二份答案”；`MaterialSurfaceTests` 固定了两条分支的差别（有材质 → alpha 0；无材质 → alpha 0xF5） |
| **O-2 的验证结论（实测后修正）** | 规则：`WidgetRuntimeHost.Add` 在 `Show()`+`Start()` 都返回后才 `Subscribe`。**单元测试确实构造不出有鉴别力的用例**：`TickService` 由 `DispatcherTimer` 驱动，`Start()` 在 UI 线程同步返回，tick 不可能插入其中；要在单元测试里区分二者必须注入可替换的调度器，而那是 D-14 禁止的新抽象。**但 desktop self-test 有真实 dispatcher，可以区分**：第 21 节加入探针 widget，在它自己的 `Start()` 内读取共享 timer 的订阅数，并与一个真实内置 widget（时钟）共用同一个 timer。5 条断言：starting 期间不在 timer 上、started 之后在 timer 上、tick 真的到达、移除后离开 timer、移除后不再被 tick。**已做反向实验（falsification）**：把 `Subscribe` 移回 `try` 之前，其中 4 条断言 **FAIL**（starting 期间订阅数 2→3；移除后仍被 tick，7→19），改回后全绿 —— 证明这些断言**能够失败**，这正是原计划缺失的证据。配套改动：`TickService.SubscriberCount`（`internal`，不是 API 面）、`CreateShell` 接受传入的 `TickService` |
| **Rollback** | 每个小步骤一个提交，可单独 `git revert`：`c4a083f`、`71beefc`、`40e909a`、`7f31b15`、`8db2eae`、`ada6a3b`、`dffda20`、`dd6c6c1`、`45527bf`、`9654490`。**先决**：P3 不改动**启动顺序**（顺序冻结在 P1 的纯函数与既有自检断言上）；O-2 是唯一被采纳的时序变更，可单独回退该订阅位置 |

---

### P4 — 抽出 `DeskKit.Runtime` 项目

| 项 | 内容 |
| --- | --- |
| 修改文件 | `DeskKit.slnx`、`App/DeskKit.App.csproj`（加 Runtime 引用；`InternalsVisibleTo` 授权自检访问 Runtime internal）、`App.axaml.cs`（组合根） |
| 新增文件 | `src/DeskKit.Runtime/DeskKit.Runtime.csproj`、`tests/DeskKit.Runtime.Tests/DeskKit.Runtime.Tests.csproj` |
| 删除文件 | App 中的 `Services/{WidgetShell,WidgetRuntime,TickService,ScreenProbe,ThemeService}.cs`、`Views/{WidgetWindow.axaml(.cs),WidgetGlowLayer.cs}`（移走） |
| 类型移动 | 见 19.3（WidgetShell/WorkspaceState/WidgetRuntimeHost/WidgetSurfaceFactory/PlacementController/AppearanceController/TickService/ScreenProbe/ScreenBoundsMapper/ThemeService/WidgetWindow/WidgetGlowLayer/WidgetRuntime/IShellFacade/ShellEnvironment → Runtime） |
| API 变化 | 命名空间 `DeskKit.App.*` → `DeskKit.Runtime.*`；`WidgetWindow` 由 App 迁至 Runtime（`DeskKit.App.Views` → `DeskKit.Runtime.Views`） |
| 测试变化 | `App.Tests` 中与 Runtime 有关的测试迁到 `Runtime.Tests`；新增**依赖约束测试**（见下） |
| 行为验证 | build/test + 自检全绿；`--selftest` 第 1–8 节（渲染/z-order/样式/隐藏/移动缩放）必须仍 PASS |
| 预期指标 | `DeskKit.Runtime.csproj` 的 `ProjectReference` **只有** `DeskKit.Core`；`dotnet list reference` 断言通过 |
| **Rollback** | 类型移动 + 命名空间，可 `git revert`；若 CI 依赖断言导致阻塞，可先移除断言再回退 |

### P4 实施记录（2026-10-02）

| 步骤 | 提交 | 内容 | 结果 |
| --- | --- | --- | --- |
| P4-0 | `f88f4a1` | **D-6 合同下沉（前置条件）**：`WidgetMaterial`（含 `FillsWindow` 扩展）、`IDesktopLayerService`+`DesktopLayerOptions`、`IWindowMaterialService` 移入 `Core/Abstractions`；`MaterialPolicy.Resolve` 与 build 常量、`DesktopLayerPolicy` 留 Platform。`FillsWindow` 测试随之移入 Core.Tests（O-5） | 完成 |
| P4-1 | 并入 P4-2 | `src/DeskKit.Runtime/DeskKit.Runtime.csproj` 建立：唯一 `ProjectReference` 为 Core；`InternalsVisibleTo` 授予 `DeskKit.App` 与 `DeskKit.Runtime.Tests`；`EnforceRuntimeBoundary` 目标在 `Build` 前校验引用。**已做反向实验**：临时加入 `DeskKit.Platform` 引用导致构建失败（`DeskKit.Runtime must reference DeskKit.Core only (got DeskKit.Platform); see D-3.`），移除后恢复 0 错误 | 完成 |
| P4-2 | `d7ad4d8` | 机制迁入 Runtime：`Views/{WidgetWindow,WidgetGlowLayer}`（`DeskKit.Runtime.Views`）、`WidgetRuntime`+`WidgetInfo`、`TickService`、`ScreenProbe`、`ThemeService`、`ScreenBoundsMapper`、`WorkspaceState`、`PlacementController`（含 `IPlaceableWidget`）、`WidgetRuntimeHost`、`WidgetSurfaceFactory`、`AppearanceController`。`WidgetShell`/`IShellFacade`/Product 协作者仍在 App。测试：`PlacementControllerTests`、`MaterialSurfaceTests` → `tests/DeskKit.Runtime.Tests`（10 个测试） | 完成 |
| P4-3 | `5567910` | `IShellFacade` → Runtime；**语言从合同中移除**（Product 自己的窗口直接依赖 `LanguageService`：`SettingsViewModel`、`SettingsWindowController`、`TrayIconController` 各接一个）；新增 `LoadReport`；新增 `ShellEnvironment`（`public` 记录：`WindowIcon? Icon` + `Action<string>? ApplyLanguage`，D-9 的唯一委托；必须是 `public`，因为 `WidgetShell` 的公开构造函数要接它） | 完成 |
| P4-4 | `dd0255d` | `WidgetShell` → `Runtime/WidgetShell.cs`（249 行）；`ShellEnvironment`/首启 ids 由构造注入；组合根接管托盘/设置窗口/右键菜单/存储告警/自启动；新增 `WidgetAdded` + `SettingsRequested` 两个接缝；托盘可见性改为跟随 `StateChanged` | 完成 |

**P4-4 实际结果（2026-10-02）**

| 项 | 结果 |
| --- | --- |
| P4-4 commit | `dd0255d`（外加 `3549c2a` P4-4a：`ShellStartup` 移入 Runtime 并接收首启 ids） |
| build | **0 警告 / 0 错误**（`dotnet build DeskKit.slnx -c Release`） |
| test | **252 通过 / 0 失败**（Core 164、Platform 40、Persistence 26、Runtime 17、App 5） |
| selftest | **PASS (214 checks)**、退出码 0（新增第 22 节 5 条） |
| 依赖边界验证 | `Core` 无引用；`Runtime`/`Persistence`/`Platform`/`Widgets` **各自仅引用 Core**；`App` 引用全部五个。`src/DeskKit.Runtime` 源码中 **不存在** `using DeskKit.{App,Widgets,Platform,Persistence}`（`git grep` 为空）。`dotnet list reference` 逐项目确认 |
| `WidgetShell` 最终行数 | **249 行**（非空非注释 140 行），`Runtime/WidgetShell.cs` |

**行为验证（逐条对应本轮要求）**

| 要确认的行为 | 证据 |
| --- | --- |
| 首次启动默认 widgets 仍正确 | self-test 第 9 节：`PASS a first run seeds exactly one widget [count=1]`、`PASS the seeded widget is a row of its own [1 rows]` |
| 已有 state / placement 能恢复 | 第 9 节：`PASS the placement survives a restart [saved 80,80 reloaded 80,80]`；第 16 节：`PASS the old layout is read [count=1]` |
| widget Start / Stop / tick 时序未回退 | 第 21 节（O-2）：`PASS a widget is not on the shared timer while it is still starting`、`PASS the tick reaches a running widget [ticks=7]`、`PASS a widget that was removed is off the shared timer` |
| theme / material 保持 P3 修复后的结果 | 第 13 节：`PASS the default background is the material and nothing else [card alpha=0/255]` |
| SettingsWidget 添加 / 设置请求能到 App | **新增第 22 节**（5 条）：seeded widget 与后加 widget 都会 `WidgetAdded` 通知 Product，Product 建的菜单确实在窗口上（`items=2`），`ShowSettings` 的请求确实到达 Product（`asked=1`） |
| 存储告警仍由 Product 展示 | 第 18 节：自检按组合根同样顺序调用 `StorageNoticePresenter(...).Show(shell.LoadReport)`，`PASS the session that cannot save says so`、`PASS the database that could not be read is untouched` |
| 托盘操作 | 托盘是 Product UI，无法自动化断言；改为**实机启动验证**：从 `bin\Release\net10.0\DeskKit.App.exe` 启动真实应用 10 秒（真实 DI + 组合根 + 托盘 + 设置窗口 + 自启动对齐 + 菜单订阅），进程存活、**stderr/stdout 为空**、日志无 `DeskKit failed to start`、无残留进程。DI 注册错误或不接线异常会在此路径上暴露 |
| Persistence / Platform 只由 App 组合 | `dotnet list reference`：只有 `DeskKit.App` 同时引用 Persistence 与 Platform（其余项目仅 Core） |

**P4-4 期间与计划的两处细节差异（已确认无行为回退）**：① 存储告警由组合根展示（与 §19.3 一致，`INoticePresenter` 仍是 Runtime 存储问题的能力端口，只是提示文本属 Product，故调用点在 Product）；② 托盘可见性不再由 `ApplySettings` 推送，而是 `TrayIconController` 订阅 `StateChanged` 后按 `State.Settings.ShowTrayIcon` 设置——状态成为唯一事实源，`ShowTrayIcon` 的任何改动都会到达托盘。
**每个已提交步骤均验证**：build 0 警告 / 0 错误；测试 252 通过 / 0 失败（Core 164、Platform 40、Persistence 26、App 12、Runtime 10）；自检 PASS (209 checks)、退出码 0；`dotnet list src\DeskKit.Runtime reference` 仅 `DeskKit.Core`。

**P4-2 对 §19.4 的一处修正**：`NullDesktopLayerService` / `NullWindowMaterialService` **移入 Core/Abstractions**（原计划写“留 Platform”）。原因由代码给出：`WidgetWindow` 的便捷构造需要“无平台”实现，而 Runtime 不能引用 Platform；这两个类不含任何平台代码，且它们实现的合同已在 Core。若留在 Platform，就只剩“在 Runtime 里再复制一份 no-op”这一更差的选项。

**P4-4 设计（已定，执行时不需要重新做架构决策）**

| 项 | 决定 |
| --- | --- |
| `WidgetShell` 位置 | `Runtime/WidgetShell.cs`，`namespace DeskKit.Runtime`；仍只实现 `IWidgetHost` + `IShellFacade` + `IDisposable`，仍 <250 行 |
| 构造依赖 | `IStateStore`、`WidgetRegistry`、`IDesktopLayerService`、`TickService`、`ThemeService`、`IWindowMaterialService`、`ShellEnvironment`、`IWidgetMessageBus`、`IReadOnlyList<string> firstRunWidgetIds`、`ILogger<WidgetShell>`。**去掉** `IAutoStartService`（Platform）与 `LanguageService`（App）。**实际结果**：`INoticePresenter` **不在** `WidgetShell` 构造中——存储告警由组合根从 `LoadReport` 展示（见下方差异 ①）；该端口仍是存放在 Core 的能力端口，只是 Runtime 侧不再直接持有它 |
| 首次运行播种 | `ShellStartup` 随 `WidgetShell` 移入 Runtime，构造改为 `(WidgetRegistry registry, IReadOnlyList<string> firstRunWidgetIds, ILogger logger)`：**D-4 的“Runtime 只消费要创建哪些初始 placement”** 由这个 `IReadOnlyList<string>` 承担；App 的 `DefaultLayout.WidgetIds` 是唯一内容来源，Runtime 不知道 clock 是什么 |
| 自启动 | `AutoStartController` 留 App（Product 策略）。启动对齐改由组合根在 `shell.Start()` 之后执行：`Reconcile(shell.State.Settings)` → 有修正则 `shell.ApplySettings(corrected)`。理由：它只改写存储的首选项，不与 widget 创建交互 |
| 设置窗口 / 托盘 / 存储告警 | 由组合根（`App.axaml.cs`）构造并在 `Start()` 之后启动：`SettingsWindowController(shell, assets, language)`、`TrayIconController(shell, catalog, assets, language, () => settings.Open(null))`、`StorageNoticePresenter(notices, tray).Show(shell.LoadReport)`。`WidgetShell` 不再 `new` 任何 Product UI |
| 托盘可见性 | 不再由 `ApplySettings` 直接设置：`TrayIconController` 订阅 `shell.StateChanged`，从 `shell.State.Settings.ShowTrayIcon` 读（状态是唯一事实源） |
| 右键菜单 | `WidgetRuntimeHost.Add` 去掉 `Func<WidgetRuntime, ContextMenu>` 参数；`WidgetShell` 在实例创建后抛 `event EventHandler<WidgetRuntime>? WidgetAdded`（在 `IShellFacade` 上），Product 在组合根订阅并 `runtime.Window.SetContextMenu(_contextMenus.Create(...))`（`SetContextMenu` 已是 public）。为此 `WidgetRuntime` 升为 **public**（`WidgetInfo` 已是） |
| `IWidgetHost.ShowSettings`（§21.2 硬约束，不得删除） | Runtime 无法打开 Product 的设置窗口 → 抛 `event EventHandler<WidgetViewModel>? SettingsRequested`，由 Product 订阅后打开自己的窗口。当前没有内置 widget 调用它，但必须可用 |
| `IShellFacade.Widgets` | 改为 `IReadOnlyList<WidgetRuntime> Runtimes`（`AddWidget` 返回 `WidgetRuntime?`）；名字解析留 Product：`WidgetCatalog.Describe(runtime)` 产出 `WidgetInfo`，`SettingsViewModel` 改为注入 `WidgetCatalog` |
| 语言应用 | Runtime 侧用 `ShellEnvironment.ApplyLanguage`，自身保留 `_appliedLanguage` 以免重复应用（原 `Language.Setting` 比较的等价物） |
| 离开 `WidgetShell` 的字段 | `_catalog`、`_storageNotices`、`_tray`、`_settings`、`_contextMenus`（Product）、`_startup`（进 Runtime 但改为接收 ids）、`_autoStartPolicy`（Product） |
### P5 / P6 实际结果（2026-10-02）

| 步骤 | 提交 | 内容 | 结果 |
| --- | --- | --- | --- |
| P5-0 | `f88f4a1`（P4-0） | 三个契约 + `WidgetMaterial`/`FillsWindow` 已在 P4-0 下沉 Core；`ScreenBoundsMapper` 已在 P4-2 迁 Runtime；`FillsWindow` 测试已入 Core.Tests | 完成（提前于 P4） |
| P5-1 | `0925072` | 新增 `Platform/INotificationWindowStyler.cs`（+`NullNotificationWindowStyler`）与 `Platform/Windows/WindowsNotificationWindowStyler.cs`；`NoticeWindow` 改为注入端口；`NoticePresenter` 传递；`AddDeskKitPlatform()` 注册。**生产 UI 中 `using DeskKit.Platform.Windows` 归零**（诊断层按 D-13 保留） | 完成 |
| P5-2 | `510024d` | 新增 `tests/DeskKit.App.Tests/WidgetResourceKeyTests.cs`：对每个内置 descriptor 的 Name/Description 键，在两个语言文件中断言存在且非空；另有一条同义守卫（catalogue 少描述一个 widget 时理论会静默通过）与一条“检查会失败”的元测试。**已做反向实验**：把 `Strings.json` 里一个键改名 → 理论失败并指出缺少的键；还原后全绿（还原必须字节级，重新保存会给该文件加 BOM，资源生成器会因此报错，自检捕获到了） | 完成 |
| P5-3 | `5288729` | D-5：`WidgetContext.Placement` 增加 XML 说明（创建时快照，不跟随窗口；保留名称不改 `InitialPlacement`），`Settings`/`Host` 同时补说明 | 完成 |
| P5-4 | `35ed802` | 文档：`docs/architecture.md` 改为 6 项目 + 三层边界 + 依赖方向，修正 §13.1 全部不一致项（Core 拆盒、StateStore 措辞、WidgetShell 职责、能力契约归属 + `INotificationWindowStyler`）；`README.md` 更新 Project layout（6 项目）、Tests、扩展模型、语言归属、Adding a widget | 完成 |
| P6 | `41b59e2` | 仅文档：`docs/architecture.md` 新增扩展性章节（已稳定的契约、运行时故意不知道什么、为什么还没有 loader、五个必须先定的策略、为扩展保留的接缝）；README 扩展点段落同步。**计划中的可选 `IWidgetProviderSource` 未采纳**：它唯一可能的实现就是 loader，当前既无实现也无调用方，属于猜测而非接缝，且其设计依赖尚未决定的五项策略（与 N-1 一致） | 完成 |

**P5/P6 验证**：build **0 警告 / 0 错误**；测试 **260 通过 / 0 失败**（Core 164、Platform 40、Persistence 26、Runtime 17、App 13）；自检 **PASS (214 checks)**、退出码 0、**0 FAIL**、第 1–22 节全部存在；第 13 节（window material）与第 19 节（notice window：不在任务栏、不在 Alt+Tab、从不被激活）仍 PASS。
**D-3 的检查方式（实现建议）**：在 `Runtime.csproj` 加一个 MSBuild 校验目标（或 CI 步骤）：

```xml
<Target Name="EnforceRuntimeBoundary" BeforeTargets="Build">
  <Error Condition="'%(ProjectReference.Filename)' != 'DeskKit.Core'"
         Text="DeskKit.Runtime must reference DeskKit.Core only (got %(ProjectReference.Filename))." />
</Target>
```

---

### P5 — 契约 / 平台 / 本地化 / 文档整理

| 项 | 内容 |
| --- | --- |
| 修改文件 | `Core/Abstractions/*`（新契约）、`Platform/*`（契约移走）、`Runtime/*`（改用 Core 契约）、`PlatformServiceCollectionExtensions.cs`（注册 `INotificationWindowStyler`）、`App/Views/NoticeWindow.cs`（去掉 `Platform.Windows`）、`docs/architecture.md`、`README.md` |
| 新增文件 | `Core/Abstractions/{IDesktopLayerService,IWindowMaterialService,WidgetMaterial}.cs`、`Platform/INotificationWindowStyler.cs`(+Null)、`Platform/Windows/WindowsNotificationWindowStyler.cs` |
| 删除文件 | `Platform/IDesktopLayerService.cs`（原契约位置）、`Platform/IWindowMaterialService.cs`、`Platform/WidgetMaterial.cs`、`Platform/ScreenBoundsMapper.cs`（已移 Runtime）；`MaterialPolicy.cs` 拆出 `FillsWindow` |
| 类型移动 | 见 19.4 |
| API 变化 | 3 个契约 + `WidgetMaterial` 命名空间 `DeskKit.Platform` → `DeskKit.Core.Abstractions`；新增 `INotificationWindowStyler`；`WidgetContext.Placement` 增加 XML 说明（D-5） |
| 测试变化 | `MaterialPolicyTests` 拆为：`FillsWindow` 断言 → `Core.Tests`；`Resolve` 断言留 `Platform.Tests`（O-5）。新增资源键完整性测试（对 `BuiltInWidgets.CreateProviders` 每个 descriptor 断言名称/描述键在两个 `Strings*.json` 中） |
| 行为验证 | build/test + 自检全绿（第 13 节 window material、第 19 节 notice window 必须仍 PASS） |
| **Rollback** | 独立提交；若 `NoticeWindow` 改动引发问题，可单独回退该文件并保留其它整理 |

---

### P6 — 扩展性设计（**只设计，不实现**）

| 项 | 内容 |
| --- | --- |
| 修改文件 | `docs/architecture.md`（扩展点章节）、`README.md` |
| 新增文件 | 可选 `Core/Abstractions/IWidgetProviderSource.cs`（**仅契约，无实现**） |
| 删除文件 | 无 |
| 类型移动 | 无 |
| API 变化 | 无（可选契约若采纳则为纯新增） |
| 测试变化 | 无 |
| 行为验证 | 不变（`WidgetRegistry` 重复 ID 行为保持） |
| **Rollback** | 仅文档；无风险 |

**阶段依赖**：`P0 → P1 → P2 → P3 → P4 → P5 → P6`（严格顺序）。
不允许跳过 P1 直接做 P2/P3：规则若还埋在 `WidgetShell` 里，搬迁会同时改动持久化与编排逻辑，无法判断回归来源。
不允许跳过 P3 直接做 P4：一次性同时“拆类 + 移项目”会让失败无法二分定位。

---

## 21. API / Contract Changes

### 21.1 新增（全部为加法或内部）

| 变更 | 兼容性 |
| --- | --- |
| `Core.Abstractions.IStateStore` | 新增 |
| `Core.Abstractions.INoticePresenter`（+`Notice` +`NullNoticePresenter`） | 新增（自 App 迁入） |
| `Core.Abstractions.IDesktopLayerService`（+`DesktopLayerOptions`） | 命名空间变化 |
| `Core.Abstractions.IWindowMaterialService` | 命名空间变化 |
| `Core.Abstractions.WidgetMaterial`（+`FillsWindow`） | 命名空间变化 |
| `Platform.INotificationWindowStyler`（+Null） | 新增 |
| `Core.Services.WidgetSettingsMigrator` / `WidgetSeedPolicy` | 新增（静态） |
| `Core.Models.AutoStartReconciliation` / `WindowCardGeometry` | 新增（静态） |
| `Runtime.IShellFacade` / `ShellEnvironment` | 新增 |
| `WidgetWindow` 约 25 个成员 `public → internal` | 源破坏，**无外部消费者**（自检同程序集） |
| `StateStore`/`AppPaths` 命名空间 → `DeskKit.Persistence` | 源破坏，无外部消费者 |

### 21.2 保持不变（硬约束）

以下签名在整个重构中**不得改变**（允许换命名空间/程序集）：
`IWidgetProvider.{Descriptor,Create}`、`WidgetContext.{Placement,Settings,Host,InstanceId}`、`WidgetViewModel.{CreateView,CreateSettingsView,Start,Stop,Dispose}`、`IWidgetHost.{Messages,Screens,ShowSettings,RemoveWidget,RequestSave}`、`IWidgetMessageBus.{Subscribe,Publish}`、`ITickAware.OnTick`、`WidgetSettings.{Get,Set}`、`IWidgetSettingsMigrations.{SettingsVersion,Migrations}`、`WidgetPlacement` 字段集合、`AppState`/`AppSettings` 字段集合、`WidgetDescriptor` 构造（**不新增字段**，D-4）。

### 21.3 明确不做的 API 变更

- 不把 `CreateView(): Control` 改成非 Avalonia 抽象（D-1）。
- 不把 `WidgetViewModel` 从 `ObservableObject` 解耦。
- 不把 `WidgetDescriptor.DisplayName` 从资源键改为文本。
- 不给 `IWidgetHost` 增加 `IWidgetMessageBus` 之外的通用请求总线（N-2）。
- 不改像素单位约定（N-9）。
- 不加 `IWidgetWindow`/`IWidgetSurface`/`IWidgetManager`/`IWidgetRuntimeManager`/`IWidgetCatalog`（D-14）。

---

## 22. 测试策略与验证矩阵

### 22.1 原则

1. 每个新边界配一组**有真实价值**的测试（第 11.3 节），不追覆盖率。
2. 优先让逻辑可纯测，而不是引入 Avalonia 测试宿主（`Avalonia.Headless` 标 OPTIONAL，不在本轮）。
3. 不测试纯框架行为。

### 22.2 测试项目最终形态

| 项目 | 覆盖 |
| --- | --- |
| `Core.Tests` | 现有纯规则 11 + 新增 4 组（migrator/autostart/geometry/seed）+ `FillsWindow`（自 Platform.Tests 移入） |
| `Runtime.Tests` | `PlacementController`（fake rect source）、`WorkspaceState`（fake `IStateStore`）、`WidgetRuntimeHost` 生命周期顺序（fake surface factory） |
| `Persistence.Tests` | `StateStoreTests`（19+）+ 迁移兼容断言 |
| `Platform.Tests` | `DesktopLayerPolicy`、`MaterialPolicy.Resolve` |
| `App.Tests` | `SettingsViewModel`（fake `IShellFacade`）、`WidgetCatalog`、`DefaultLayout` + `WidgetSeedPolicy` 组合、资源键完整性 |

### 22.3 验证矩阵（每阶段）

| 阶段 | 单测 | 自检（开发机） | 结构断言 |
| --- | --- | --- | --- |
| P0 | 记录基线 | 记录基线 | — |
| P1 | 新增 4 组 | 全绿（9/11/12/17/19） | `WidgetWindow` public ≤30 |
| P2 | 全部 + Persistence.Tests | 全绿（16/17/18/20） | Widgets 传递依赖无 EF/SQLite |
| P3 | 新增 App.Tests | 全绿（9/10/11/14/19） | `WidgetShell` <250 行 |
| P4 | Runtime.Tests | 全绿（1–8 节） | Runtime 只引用 Core（MSBuild `Error`）；**Runtime 不含 `LanguageService`/Lingua/`DeskKit.Widgets` 引用** |
| P5 | 全部 | 全绿（13/19） | 资源键完整性通过 |
| P6 | 不变 | 不变 | 不变 |

### 22.4 不可自动化项

- `--selftest` 需真实桌面/GPU；CI 无此能力。**必须在开发机执行并人工确认 0 FAIL。**
- “已有数据库在升级后仍被识别”需要一台带 `deskkit.db` 的机器或临时构造库（P2 已给断言）。

---

## 23. 风险与缓解

| # | 风险 | 影响 | 缓解 |
| --- | --- | --- | --- |
| R-1 | **EF 迁移命名空间变化导致已有库被判为待迁移/更新 schema** | 用户库被误迁移或拒绝启动 | 已确认 EF 以 `MigrationId`（无命名空间）判定；P2 加入 `__EFMigrationsHistory` 断言 + 自检 16–18；先在临时目录造库验证。**P2 不得同时改 EF 模型** |
| R-2 | `WidgetShell` 拆分引入行为回归（启动顺序/吸附/可见性/节流） | 桌面行为异常 | P1 先把规则冻结为纯函数并单测；P3 逐块搬迁、每块后跑自检；P3 不改顺序 |
| R-3 | 自检无法在 CI 回归 | 回归漏到人工环节 | 明确“P2/P3/P4 的最终验证必须在有桌面机器执行”；以报告节数/通过数对比历史 artifact |
| R-4 | `public → internal` 导致编译或 XAML 失败 | 编译中断 | 编译期即可发现；已确认 `WidgetWindow.axaml` 不绑定这些只读访问器 |
| R-5 | 新协作者过度拆分 | 复杂度不降反升 | 每个协作者必须能回答“它独占什么数据 / 删掉会失去什么行为”；<80 行者合并回门面（D-8/D-9） |
| R-6 | **Runtime 依赖 App 资源（应用图标）导致禁止边被破坏** | 架构目标落空 | 由 App 注入 `ShellEnvironment.Icon`；P4 的 MSBuild 断言会直接失败 |
| R-7 | ~~`Func<string,string>` 文本注入被滥用为隐藏耦合~~ **已消除** | — | **O-1 已否决**：不注入文本委托，`ShellEnvironment` 只含 `Icon` + `ApplyLanguage`；`WidgetWindow.Title` 用稳定内部值（`descriptor.Id`） |
| R-8 | `IShellFacade` 被当作万能门面继续膨胀 | 新的 God Object | `IShellFacade` 只暴露 Product UI 需要的成员（State/Widgets/AvailableWidgets/Add/Remove/ApplySettings/SetWidgetsVisible/StateChanged/LoadReport/Start/Dispose）；新增成员需评审 |
| R-9 | P4 一次性搬迁过多类型导致失败难定位 | 回退成本高 | P4 内部按 4 步提交：①纯 DTO/服务（Tick/ScreenProbe/ThemeService）②表面（WidgetWindow/GlowLayer）③协作者 ④facade；每步跑 build/test |
| R-10 | `DesktopLayerSelfTest` 依赖 Runtime internals 而 Runtime 在另一程序集 | 自检无法编译 | `Runtime.csproj` 加 `InternalsVisibleTo("DeskKit.App")`（D-13） |
| R-11 | 资源键完整性测试只覆盖内置 provider | 插件键仍可能缺失 | 测试只覆盖本仓库 provider；插件期（P6 之后）再定义校验契约 |
| R-12 | `IStateStore` 被认为是多余抽象 | 增加间接层 | 它有 2 个消费者（`WorkspaceState`、测试）与 1 个实现；若 P3 发现无法脱库测试，则降级为具体类型 |
| R-13 | 平台契约下沉造成大范围 `using` 改动 | 机械改动多，易出错 | P5 独立成阶段提交；`MaterialPolicy` 只拆 `FillsWindow`；不做其它移动 |
| R-14 | `AppPaths` 迁到 Persistence 后日志路径归属模糊 | 概念不清 | 保留 `AppPaths` 于 Persistence（O-3 默认不拆），App 引用它取日志路径 |

---

## 24. Definition of Done

### 24.1 结构（可客观检查）

- [x] `src/` 下恰好 6 个项目：`DeskKit.Core`、`DeskKit.Runtime`、`DeskKit.Persistence`、`DeskKit.Platform`、`DeskKit.Widgets`、`DeskKit.App`。
- [x] `tests/` 下 5 个测试项目（Core/Runtime/Persistence/Platform/App）。
- [x] 依赖满足第 15.2 节全部**允许边**，且**全部禁止边成立**（CI/MSBuild 断言）。
- [x] `DeskKit.Core.csproj` 的 `PackageReference` 只有 Avalonia、CommunityToolkit.Mvvm、Logging.Abstractions（**无 EF Core/SQLite**）。
- [x] `DeskKit.Runtime.csproj` 的 `ProjectReference` **只有** `DeskKit.Core`。
- [x] `DeskKit.Widgets` 传递依赖中不含 EF Core/SQLite（`dotnet list package --include-transitive` 验证）。
- [x] `DeskKit.App` 的**生产 UI** 代码中不再有 `using DeskKit.Platform.Windows`（诊断代码除外，D-13）。

### 24.2 代码质量（有数字）

- [x] `App/Services/WidgetShell.cs` 不存在；`Runtime/WidgetShell.cs` < **250 行**。
- [x] `WidgetShell` 不再持有 `StateStore`/`TrayIcon`/`SettingsWindow`/`DispatcherTimer` 字段；不引用 `DeskKit.Widgets`。
- [x] `WorkspaceState` 不含 migration/autostart/window/UI 逻辑（D-8）。
- [x] `SettingsViewModel` 依赖 `IShellFacade`；文件中不出现 `WidgetShell`。
- [x] `WidgetWindow.axaml.cs` public 成员 ≤ **30**；自检访问器为 `internal`。
- [x] `WidgetDescriptor` **没有**播种相关字段（D-4）；`DeskKit.Widgets` 无播种逻辑。
- [x] 不存在 `IWidgetWindow`/`IWidgetSurface`/`IWidgetManager`/`IWidgetRuntimeManager`/`IWidgetCatalog`（D-14）。

### 24.3 行为

- [x] `dotnet build DeskKit.slnx -c Release` 0 warning / 0 error。
- [x] `dotnet test DeskKit.slnx -c Release` 全部通过，测试总数 ≥ 基线 + 新增（记录数字）。
- [x] **在开发机重新执行** `--selftest`：退出码 0、报告 **0 FAIL**、第 1–20 节全部存在且 PASS（历史 artifact：21 节 / 203 PASS / 0 FAIL，仅作对照）。
- [x] 既有 `deskkit.db` 在升级后仍识别为“已应用全部迁移”，不触发 `NewerSchema`/`Unavailable`/迁移备份。
- [x] Widget SDK 公共签名与第 21.2 节清单逐一相同。
- [x] 生命周期顺序 5 条规则全部保持（自检第 4/5/6/7/8 节 + 第 9 节端到端）。

### 24.4 文档

- [x] `docs/architecture.md` 更新为 Framework / Runtime / Product 三层 + 6 项目，并修正第 13.1 节全部不一致项。
- [x] `README.md` 更新“Project layout”（6 项目）、“Adding a widget”（种子行为与 `DefaultLayout` 一致）、扩展点章节（保留扩展点但**未实现**加载）。
- [x] `plan.md`（仓库根）与本文件一致；`architecture-review-final.md` 作为决策依据一并放入仓库或明确不提交。

### 24.5 红线（违反即视为未完成）

- 任何一步导致 `--selftest` FAIL，必须**回退该步**，不得修改自检阈值或期望来“通过”。
- 不得为通过测试把 `WidgetWindow`/`WidgetGlowLayer` 成员重新 public 化。
- 不得以任何形式把 EF Core 依赖重新引入 `DeskKit.Core`，或把 `DeskKit.Widgets`/`Persistence`/`App` 引入 `DeskKit.Runtime`。
- 不得为“依赖图好看”把 Windows 版本号、`Interop`、Win32 常量搬进 `Core`。

---

### 24.6 验收记录（2026-10-02，全部 23 项已核对）

| 条目 | 证据 |
| --- | --- |
| 24.1 ① 6 个项目 | `src/` = App, Core, Persistence, Platform, Runtime, Widgets |
| 24.1 ② 5 个测试项目 | `tests/` = App.Tests, Core.Tests, Persistence.Tests, Platform.Tests, Runtime.Tests |
| 24.1 ③ 允许/禁止边 | `dotnet list reference`：Core 无；Runtime/Persistence/Platform/Widgets → Core；App → 全部五个。MSBuild：`EnforceRuntimeBoundary`（已用反向实验证明会拦下 Platform 引用） |
| 24.1 ④ Core 包 | Avalonia、CommunityToolkit.Mvvm、Microsoft.Extensions.Logging.Abstractions；`git grep EntityFramework\|Sqlite src/DeskKit.Core` 为空 |
| 24.1 ⑤ Runtime 引用 | 仅 `..\DeskKit.Core\DeskKit.Core.csproj` |
| 24.1 ⑥ Widgets 传递依赖 | `dotnet list src\DeskKit.Widgets package --include-transitive` 不含 EntityFramework/Sqlite |
| 24.1 ⑦ App 生产 UI 无 Win32 | `git grep DeskKit.Platform.Windows` 在 App 的 `Views/Shell/Services/ViewModels` 下为空；仅 `Diagnostics/*`（D-13 允许） |
| 24.2 ① 门面规模 | `App/Services/WidgetShell.cs` 不存在；`src/DeskKit.Runtime/WidgetShell.cs` = **249 行**（非空非注释 140） |
| 24.2 ② 门面字段 | 文件中 `TrayIcon`/`SettingsWindow`/`DispatcherTimer`/`WidgetCatalog`/`ShellAssets`/`AutoStart` 命中数均为 0；`StateStore` 唯一命中是端口 `IStateStore` |
| 24.2 ③ WorkspaceState | `git grep Migrat\|AutoStart\|TrayIcon\|SettingsWindow\|ContextMenu\|Notice\|Window` 在该文件为空 |
| 24.2 ④ SettingsViewModel | 只依赖 `IShellFacade` + `LanguageService` + `WidgetCatalog`；文件中不出现 `WidgetShell` |
| 24.2 ⑤ WidgetWindow | public 成员 **19**（≤30）；internal 32（自检访问器） |
| 24.2 ⑥ 描述符/播种 | `WidgetDescriptor` 字段与基线完全一致（无播种字段）；`git grep SeedOnFirstRun\|DefaultLayout\|FirstRun` 在 `DeskKit.Widgets` 为空 |
| 24.2 ⑦ 被禁止的接口 | `git grep IWidgetWindow\|IWidgetSurface\|IWidgetManager\|IWidgetRuntimeManager\|IWidgetCatalog` 在 `src`+`tests` 为空 |
| 24.3 ① 构建 | `dotnet build DeskKit.slnx -c Release`（含 `--no-incremental`）：**0 警告 / 0 错误** |
| 24.3 ② 测试 | **260 通过 / 0 失败**（基线 189；Core 164、Platform 40、Persistence 26、Runtime 17、App 13） |
| 24.3 ③ 自检（本机重跑） | `exit=0`、**0 FAIL**、**PASS (214 checks)**、第 1–22 节全部存在且 PASS（历史 artifact 为 21 节 / 203 PASS，仅作对照） |
| 24.3 ④ 既有数据库兼容 | `MigrationCompatibilityTests`（4 项，绿）钉住 `__EFMigrationsHistory` 记录的是迁移 Id；P2 阶段在本机真实 `%LOCALAPPDATA%\DeskKit\deskkit.db` 上的实测结果（`recorded=20261002062527_InitialCreate`、`outcome=Loaded`、未触发 `NewerSchema`/`Unavailable`/迁移备份）仍然成立：`DeskKit.Persistence` 自 P2 起仅有一次**注释级**改动（`ada6a3b`，diff 仅 `///` 行）。**未在本轮重新对真实库执行探针**（自检第 20 节确认本轮所有自检都未触碰真实库） |
| 24.3 ⑤ SDK 签名 | 逐条核对 §21.2 清单：`IWidgetProvider`、`WidgetContext`、`WidgetViewModel`、`IWidgetHost`、`IWidgetMessageBus`、`ITickAware`、`WidgetSettings`、`IWidgetSettingsMigrations`、`WidgetPlacement`、`AppState`/`AppSettings`、`WidgetDescriptor` 构造——全部一致 |
| 24.3 ⑥ 生命周期 5 规则 | 自检第 4/5/6/7/8 节 + 第 9 节端到端全 PASS；第 21 节另证明 `ITickAware` 只在 `Start()` 之后订阅 |
| 24.4 ① 架构文档 | 已改为 6 项目 + 三层边界 + 依赖方向 + 能力契约归属 |
| 24.4 ② README | Project layout（6 项目）、Tests、扩展模型、语言归属、Adding a widget（含“新 widget 不在 `DefaultLayout` 里就不会被首启创建”） |
| 24.4 ③ 计划归档 | `plan.md` 已放入仓库根；`architecture-review-final.md` **不提交**（第三轮已裁定只保留 `plan.md`，被否决方案不再作为独立方案存在） |
| 24.5 红线 | ① 任何一步都未让自检 FAIL（每次提交前都跑）；② `WidgetWindow` public 从 51 → 19 后未回涨（本轮新增的菜单读取是 `internal`）；③ Core 无 EF，Runtime 无 Widgets/Persistence/App；④ Core 不再出现任何 Win32 符号（`DesktopLayerOptions.PreventActivation` 的说明改为描述行为，不再点名平台样式） |

**尚未由自动化覆盖、已在文档中如实标注的两点**：托盘图标操作无法在自检中断言（Product UI），改为实机启动验证（真实 DI 下运行 10 秒、stderr/stdout 为空、日志无启动失败、无残留进程）；§24.3 ④ 见上。

### 24.7 Post-refactor audit（最终只读审计，commit `a5cb6e2`）

| 检查项 | 实际结果 |
| --- | --- |
| 依赖边（`dotnet list reference`） | Core → 无；Runtime/Persistence/Platform/Widgets → Core；App → 五个全部。与 §16 目标图逐条一致 |
| 最终验证 ① build | `dotnet build DeskKit.slnx -c Release --no-incremental` → **0 警告 / 0 错误**（40.39s，SDK `11.0.100-rc.1.26425.128`） |
| 最终验证 ② test | `dotnet test DeskKit.slnx -c Release` → **260 通过 / 0 失败**（Core 164、Platform 40、Runtime 17、Persistence 26、App 13） |
| 最终验证 ③ selftest | `--selftest --out artifacts/selftest-report.txt` → 退出码 **0**、报告 **0 处 FAIL**、**PASS (214 checks)**、第 1–22 节全部存在（历史 artifact 为 21 节 / 203 PASS，仅作对照，不是本轮结果） |
| 代码卫生 | 无残留旧命名空间；`TODO`/`FIXME`/`HACK`/`NotImplementedException` 全仓库为空；无 `AssemblyLoadContext`/DLL loader、无 MediatR/CQRS/EventBus、无 Generic Repository/UoW |
| 工作树 | `git status --porcelain` 为空；HEAD `a5cb6e2`；仓库共 42 次提交，P1→P6 阶段提交连续可回溯（**P0 没有提交，是设计如此**：该阶段只执行三项命令并记录基线） |
| 文档一致性 | `plan.md` / `docs/architecture.md` / `README.md` 与代码一致；本轮修正 3 处记录级不一致（见下），**未改生产代码** |

**本轮修正的记录级不一致（纯文档）**

1. `ShellEnvironment` 在 P4-3 / P4-4 记录中写作 `internal`，实际是 `public`——公开的 `WidgetShell` 构造函数必须接受它。
2. P4-4 设计表把 `INoticePresenter` 列为 `WidgetShell` 的构造依赖；实际**不在**（存储告警由组合根从 `LoadReport` 展示，即本节差异 ①）。
3. `docs/architecture.md`：把“去抖动写入”的主语由 “the shell” 改为 `WorkspaceState`；并把 O-6（第三方 widget id 命名规则）登记进插件策略清单第 1 条。

**已知但本轮刻意不修（非架构问题，记录备查）**

| 项 | 位置 | 影响 | 建议 |
| --- | --- | --- | --- |
| ~~视图仍自行判断平台~~ | `src/DeskKit.App/Views/NoticeWindow.cs:104` 原为 `OperatingSystem.IsWindows()` | **已修**：改为询问 `_styler.IsSupported`，`INotificationWindowStyler.IsSupported` 因此有了唯一消费者，视图不再知道操作系统 | 已完成，见 §24.8 |
| 9 处未使用的 `using DeskKit.Runtime…;`（P4 批量改命名空间遗留） | `src/DeskKit.App/Diagnostics/DesktopLayerSelfTest.{Desktop,Storage}.cs`、`src/DeskKit.App/Shell/{AutoStartController,DefaultLayout,ShellAssets,StorageNoticePresenter,WidgetContextMenuFactory}.cs`、`tests/DeskKit.App.Tests/AutoStartControllerTests.cs` | 编译无影响（构建 0 警告，当前无 IDE0005 门禁） | 随下一次功能改动顺手清理，不必单独立项 |
| 3 处注释仍把文本/图标解析写成 “the shell” 的职责 | `src/DeskKit.Core/Models/WidgetDescriptor.cs:8`、`src/DeskKit.Widgets/Clock/ClockWidgetProvider.cs:15`、`src/DeskKit.App/Shell/ShellAssets.cs:8` | 与 P4-4 后的实际归属（Product 的 `WidgetCatalog` / `ShellAssets`）不符 | 纯注释，下次接触这三个文件时改正 |

**结论**：P0–P6 重构在本轮审计后停止。后续在该基线上做正常功能开发；上表三项是记录项，不构成新的重构轮次。

### 24.8 平台选择改为编译期（审计后续改动，非 P0–P6 阶段）

| 项 | 结果 |
| --- | --- |
| 决策 | 平台实现不再用运行时判断：`DeskKit.Platform` 多目标 `net10.0;net10.0-windows`，`WINDOWS` 符号决定编译哪一套实现；`AddDeskKitPlatform()` 用 `#if WINDOWS` / `#else` 注册，原先五处 `OperatingSystem.IsWindows()` 三元判断全部删除 |
| 条件编译范围 | `Interop/{NativeMethods,Win32}.cs`、`Windows/{DesktopDiagnostics,WindowsAutoStartService,WindowsDesktopLayerService,WindowsNotificationWindowStyler,WindowsShellIconLoader,WindowsWindowMaterialService}.cs` 整文件包在 `#if WINDOWS` … `#endif` 内 |
| 运行时判断清理 | Windows 实现中 `IsSupported => true`；`Attach` / `Detach` / `TryGetState` / `Apply` / `IsEnabled` / `SetEnabled` / `LoadAsync` 内的 `OperatingSystem.IsWindows()` 全部删除。例外：`WindowsWindowMaterialService.IsSupported` 仍调 `MaterialPolicy.Resolve`，因为它回答的是“这台机器的 Mica 是否可用”（依赖 build 号），不是“是不是 Windows” |
| 目标框架 | `DeskKit.App`、`DeskKit.App.Tests` → `net10.0-windows`；`DeskKit.Platform` 必须先清空继承自 `Directory.Build.props` 的 `TargetFramework`，否则 SDK 判定为非 cross-targeting，只会构建中立 leg（本步已实测踩到） |
| 保留的运行时判断 | 仅自检第 1 节 `Check("running on Windows", …)` 与随后的早退：它回答“我在哪台机器上跑”，不是“选哪个实现” |
| 未变的边界 | 六个项目的引用方向不变；`Runtime → Core` 不受影响；`DeskKit.Platform.Tests` 留在 `net10.0`，因此它只覆盖纯规则（`DesktopLayerPolicy` / `MaterialPolicy`），这也顺带证明这两个规则类型确实与平台无关 |
| 验证 | build **0 警告 / 0 错误**；**260 测试通过 / 0 失败**；自检 **PASS (214 checks)** 退出码 0；两 leg 产物核对：`net10.0` 无 Windows 实现、`net10.0-windows` 有；实机启动 14 秒，进程存活、stderr 为空、创建 `clock` / `quick-launch` / `sticky-note` 三个默认 widget 窗口 |

## 附：证据索引

| 结论 | 证据 |
| --- | --- |
| Core 持有 EF/SQLite | `src/DeskKit.Core/DeskKit.Core.csproj` |
| WidgetShell 947 行 / 7 类职责 | `src/DeskKit.App/Services/WidgetShell.cs` 全文 |
| Runtime 依赖内置 widget 本地化 | `src/DeskKit.App/Services/WidgetShell.cs:14,109,438,833` |
| 宿主依赖具体 widget | `src/DeskKit.App/Services/WidgetShell.cs:13,354` |
| UI 依赖 Platform.Windows | `src/DeskKit.App/Views/NoticeWindow.cs:5` |
| WidgetWindow 51 public / ~25 诊断成员 | `src/DeskKit.App/Views/WidgetWindow.axaml.cs` 全文 |
| `WidgetContext.Placement` 为快照且无人读取 | `WidgetShell.CapturePlacement`；全仓库 `Context.Placement` 检索为空 |
| `FillsWindow` 仅被 Runtime 消费 | `ThemeService.cs:49`、`WidgetWindow.axaml.cs:89,101` |
| `MaterialPolicy.Resolve` 仅被 Windows 实现消费 | `WindowsWindowMaterialService.cs:60,71` |
| 无 widget 窗口标题读取 | 全仓库 `Title` 检索 |
| 无层级违规 | 全仓库 `using DeskKit.*` 检索 |
| 迁移 Id 与命名空间无关 | `Core/Data/Migrations/20261002062527_InitialCreate.cs` |
| 桌面行为历史基线 | `artifacts/selftest-report.txt`（21 节 / 203 PASS / 0 FAIL，2026-10-02 16:55:50） |
| CI 只跑 build+test | `.github/workflows/ci.yml` |
| 文档不一致 | `docs/architecture.md`、`README.md` |
