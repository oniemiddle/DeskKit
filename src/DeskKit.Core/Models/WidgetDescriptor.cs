namespace DeskKit.Core.Models;

/// <summary>
/// Static description of a widget type: how it is identified, how big it starts
/// and how small it may get.
/// </summary>
/// <param name="DisplayName">
/// The resource key a shell resolves to the widget's translated name, not the
/// name itself. A widget's name has to change with the UI language, and a value
/// captured here at startup could not.
/// </param>
/// <param name="Description">The resource key for the widget's translated description.</param>
public sealed record WidgetDescriptor(
    string Id,
    string DisplayName,
    string? Description,
    double DefaultWidth,
    double DefaultHeight,
    double MinWidth,
    double MinHeight,
    /// <summary>
    /// True for widgets that must not take keyboard focus when clicked, so
    /// interacting with them does not pull focus away from the current
    /// application. Widgets that genuinely need typing must set this false.
    /// </summary>
    bool PreventActivation,
    /// <summary>True when only one instance of this widget may exist.</summary>
    bool SingleInstance = false);
