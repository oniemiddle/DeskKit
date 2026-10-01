namespace DeskKit.Core.Models;

/// <summary>
/// Static description of a widget type: how it is identified, how big it starts
/// and how small it may get.
/// </summary>
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
