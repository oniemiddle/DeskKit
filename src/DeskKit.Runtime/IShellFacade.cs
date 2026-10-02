using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;

namespace DeskKit.Runtime;

/// <summary>
/// Everything the product's own UI needs from the thing that runs the widgets.
/// </summary>
/// <remarks>
/// The settings window and the tray talk to the shell through this rather than through
/// the concrete type, so neither of them has to know how widgets are created in order to
/// show a list of them. It is deliberately the smallest set that has a caller: a member
/// is added when something needs it, not because it might.
/// <para>
/// Nothing here is about the language the UI is displayed in. The runtime applies the
/// preference it is told to apply (see <see cref="ShellEnvironment.ApplyLanguage"/>), and
/// the product's own windows read and react to its language service directly.
/// </para>
/// </remarks>
public interface IShellFacade
{
    /// <summary>The live preferences and placed widgets.</summary>
    AppState State { get; }

    /// <summary>Raised after the state changes, so open UI can re-read it.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// What the last load found. The product is what explains a session that could not
    /// read its stored state, and nothing else is in a position to know.
    /// </summary>
    StoreLoadReport LoadReport { get; }

    /// <summary>
    /// Raised for every widget as it appears, before it is shown. The product is what
    /// hangs its own chrome on the window; the runtime never builds a menu.
    /// </summary>
    event EventHandler<WidgetRuntime>? WidgetAdded;

    /// <summary>
    /// Raised when a widget asks for its own settings. The runtime owns no settings
    /// window, so the product answers this.
    /// </summary>
    event EventHandler<WidgetViewModel>? SettingsRequested;

    /// <summary>
    /// The widgets currently placed. What to call one is the product's to resolve, so no
    /// display name is carried here.
    /// </summary>
    IReadOnlyList<WidgetRuntime> Runtimes { get; }

    /// <summary>The widget types this build can add.</summary>
    IReadOnlyList<IWidgetProvider> AvailableWidgets { get; }

    /// <summary>Places a new widget of the given type.</summary>
    WidgetRuntime? AddWidget(IWidgetProvider provider);

    /// <summary>Removes a placed widget.</summary>
    void RemoveWidget(WidgetViewModel widget);

    /// <summary>Stores a whole set of preferences and makes the running application match.</summary>
    void ApplySettings(AppSettings settings);

    /// <summary>Shows or hides every widget at once.</summary>
    void SetWidgetsVisible(bool visible);
}