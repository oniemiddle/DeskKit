using DeskKit.App.Services;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;

namespace DeskKit.App.Shell;

/// <summary>
/// Everything the product's own UI needs from the thing that runs the widgets.
/// </summary>
/// <remarks>
/// The settings window and the tray talk to the shell through this rather than through
/// the concrete type, so neither of them has to know how widgets are created in order to
/// show a list of them. It is deliberately the smallest set that has a caller: a member
/// is added when something needs it, not because it might.
/// </remarks>
public interface IShellFacade
{
    /// <summary>The live preferences and placed widgets.</summary>
    AppState State { get; }

    /// <summary>Raised after the state changes, so open UI can re-read it.</summary>
    event EventHandler? StateChanged;

    /// <summary>The language preference and the managers it drives.</summary>
    LanguageService Language { get; }

    /// <summary>The widgets currently placed, with the names to show for them.</summary>
    IReadOnlyList<WidgetInfo> Widgets { get; }

    /// <summary>The widget types this build can add.</summary>
    IReadOnlyList<IWidgetProvider> AvailableWidgets { get; }

    /// <summary>Places a new widget of the given type.</summary>
    WidgetInfo? AddWidget(IWidgetProvider provider);

    /// <summary>Removes a placed widget.</summary>
    void RemoveWidget(WidgetViewModel widget);

    /// <summary>Stores a whole set of preferences and makes the running application match.</summary>
    void ApplySettings(AppSettings settings);

    /// <summary>Shows or hides every widget at once.</summary>
    void SetWidgetsVisible(bool visible);
}
