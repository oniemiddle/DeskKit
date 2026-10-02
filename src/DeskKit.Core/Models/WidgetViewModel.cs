using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskKit.Core.Services;

namespace DeskKit.Core.Models;

/// <summary>
/// Everything a widget instance needs to run: where it sits, what it is
/// configured with, and the services the shell exposes.
/// </summary>
public sealed class WidgetContext(
    WidgetPlacement placement, WidgetSettings settings, IWidgetHost host)
{
    /// <summary>
    /// Where the widget was when it was created: a snapshot, not a live view.
    /// </summary>
    /// <remarks>
    /// Moving or resizing a widget updates the placement the shell stores, and this copy
    /// does not follow it. A widget that needs its current position asks the window it is
    /// drawn in, or reads it back from the state, rather than reading it here.
    /// </remarks>
    public WidgetPlacement Placement { get; } = placement;

    /// <summary>This instance's own configuration, shared with the stored placement.</summary>
    public WidgetSettings Settings { get; } = settings;

    /// <summary>What a widget may ask of the shell: screens, its messages, and its own state.</summary>
    public IWidgetHost Host { get; } = host;

    public string InstanceId => Placement.InstanceId;
}

/// <summary>
/// Base class for a widget's view model. One instance exists per placed widget.
/// </summary>
public abstract class WidgetViewModel(WidgetContext context) : ObservableObject, IDisposable
{
    protected WidgetContext Context { get; } = context;

    protected WidgetSettings Settings => Context.Settings;

    protected IWidgetHost Host => Context.Host;

    public string InstanceId => Context.InstanceId;

    /// <summary>Builds the control shown inside the widget window.</summary>
    public abstract Control CreateView();

    /// <summary>
    /// Builds the settings UI shown in the settings window, or null when the
    /// widget has nothing to configure.
    /// </summary>
    public virtual Control? CreateSettingsView() => null;

    /// <summary>Called once the widget has been shown.</summary>
    public virtual void Start()
    {
    }

    /// <summary>Called before the widget is torn down.</summary>
    public virtual void Stop()
    {
    }

    public virtual void Dispose()
    {
    }
}

/// <summary>
/// Implemented by widgets that want a periodic callback. The shell drives every
/// tick-aware widget from one shared timer rather than one timer per widget.
/// </summary>
public interface ITickAware
{
    void OnTick(DateTimeOffset now);
}
