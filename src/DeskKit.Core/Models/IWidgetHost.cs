using DeskKit.Core.Abstractions;

namespace DeskKit.Core.Models;

/// <summary>Services a widget may use without knowing anything about the shell.</summary>
public interface IWidgetHost
{
    /// <summary>
    /// Sends and receives explicit data contracts between widgets without giving
    /// one widget a reference to another widget's implementation.
    /// </summary>
    IWidgetMessageBus Messages { get; }

    /// <summary>Current monitor layout, in physical pixels.</summary>
    IReadOnlyList<ScreenBounds> Screens { get; }

    /// <summary>Asks the shell to open this widget's settings.</summary>
    void ShowSettings(WidgetViewModel widget);

    /// <summary>Asks the shell to remove this widget.</summary>
    void RemoveWidget(WidgetViewModel widget);

    /// <summary>Tells the shell that persisted state changed and should be written out.</summary>
    void RequestSave();
}
