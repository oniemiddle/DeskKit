using Avalonia;
using Avalonia.Controls;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using Microsoft.Extensions.Logging;

namespace DeskKit.Runtime;

/// <summary>
/// Owns the widgets that are on the desktop: which instances exist, when they are
/// started and stopped, and which of them are ticked.
/// </summary>
/// <remarks>
/// This is where a widget's life actually runs. The order below is the contract the
/// widgets are written against - shown, then started, then ticked - and it is the order
/// the desktop self test asserts.
/// <para>
/// It does not own the state those widgets are stored in, or the windows they are drawn
/// in beyond creating them: <see cref="WorkspaceState"/> owns the state, and
/// <see cref="PlacementController"/> owns where a window ends up.
/// </para>
/// </remarks>
internal sealed class WidgetRuntimeHost(
    WidgetRegistry registry,
    PlacementController placementRules,
    TickService ticks,
    WorkspaceState workspace,
    IDesktopLayerService desktopLayer,
    double surfaceMargin,
    WidgetSurfaceFactory surfaces,
    ILogger logger) : IDisposable
{
    private readonly List<WidgetRuntime> _widgets = [];

    private readonly WidgetVisibilityAnimator _animation = new(desktopLayer, logger);

    public IReadOnlyList<WidgetRuntime> Runtimes => _widgets;

    /// <summary>Starts driving the widgets that asked for a periodic callback.</summary>
    public void StartTicking() => ticks.Start();

    /// <summary>
    /// Creates one widget of the given type where a new one belongs: offset from the
    /// screen's own origin and cascaded past the widgets already placed, so adding
    /// several in a row does not stack them exactly on top of each other.
    /// </summary>
    /// <returns>Null when the widget could not be created, which is not fatal.</returns>
    public WidgetRuntime? Add(
        IWidgetProvider provider,
        IWidgetHost host,
        IReadOnlyList<ScreenBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var placement = WidgetSeedPolicy.CreatePlacement(
            provider,
            _widgets.Count,
            screens,
            Guid.NewGuid().ToString("N"));

        return Add(placement, host, screens);
    }

    /// <summary>
    /// Creates one widget: its view model, its window, its event wiring, and its start.
    /// </summary>
    /// <returns>Null when the widget could not be created, which is not fatal.</returns>
    public WidgetRuntime? Add(
        WidgetPlacement placement,
        IWidgetHost host,
        IReadOnlyList<ScreenBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(screens);

        if (registry.Find(placement.WidgetId) is not { } provider)
        {
            logger.LogWarning("Ignoring widget {WidgetId}: no provider is registered", placement.WidgetId);
            return null;
        }

        var descriptor = provider.Descriptor;
        var context = new WidgetContext(placement, new WidgetSettings(placement.Settings), host);

        // The stored position, adjusted only if the current displays cannot show it.
        var onScreen = PlacementNormalizer.EnsureOnScreen(placement, screens);

        WidgetViewModel viewModel;
        try
        {
            viewModel = provider.Create(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Widget {WidgetId} failed to be created", placement.WidgetId);
            return null;
        }

        var window = surfaces.Create(placement, descriptor, viewModel, new PixelPoint(onScreen.X, onScreen.Y));
        var runtime = new WidgetRuntime(placement, viewModel, window);


        window.SnapStrategy = proposed => placementRules.Snap(_widgets, runtime, proposed, surfaceMargin);
        window.DragCompleted += (_, _) =>
        {
            PlacementController.ClearHighlights(_widgets);
            placementRules.Capture(runtime, surfaceMargin);
        };

        window.ResizeCompleted += (_, _) => placementRules.Capture(runtime, surfaceMargin);

        _widgets.Add(runtime);

        try
        {
            window.Show();
            viewModel.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Widget {WidgetId} failed to be shown", placement.WidgetId);
            Destroy(runtime);
            return null;
        }

        // Only now is the widget running, so only now may it be ticked: a callback that
        // arrived between subscribing and starting would reach a view model that has not
        // been given its view yet.
        if (viewModel is ITickAware tickAware)
            ticks.Subscribe(tickAware);

        // The window manager does not always honour the requested origin, and a widget
        // that is stored somewhere other than where it actually sits drifts a little
        // further on every restart. Storing what the window really is removes the whole
        // class of problem.
        //
        // Compared against what was asked for, not against the stored placement: this
        // records where the window manager put the window, and must not turn a
        // display-driven adjustment into a stored one.
        if (window.Position != new PixelPoint(onScreen.X, onScreen.Y))
        {
            logger.LogInformation(
                "Widget {WidgetId} was asked for {Requested} but placed at {Actual}",
                placement.WidgetId, new PixelPoint(onScreen.X, onScreen.Y), window.Position);
            placementRules.Capture(runtime, surfaceMargin);
        }

        if (!workspace.State.Settings.WidgetsVisible)
            desktopLayer.SetVisible(window, false);

        return runtime;
    }

    /// <summary>Stops and closes one widget, and forgets it.</summary>
    public void Destroy(WidgetRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        // Before the window is closed: a slide that is still running would otherwise
        // try to move a window that no longer exists.
        _animation.Forget(runtime);

        _widgets.Remove(runtime);

        // A widget that never reached its start was never subscribed, and unsubscribing
        // something that is not there is a no-op.
        if (runtime.ViewModel is ITickAware tickAware)
            ticks.Unsubscribe(tickAware);

        try
        {
            runtime.ViewModel.Stop();
            runtime.ViewModel.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Widget {WidgetId} failed to shut down cleanly", runtime.Placement.WidgetId);
        }

        try
        {
            runtime.Window.Close();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Widget window failed to close");
        }
    }

    public WidgetRuntime? Find(WidgetViewModel viewModel) =>
        _widgets.FirstOrDefault(runtime => ReferenceEquals(runtime.ViewModel, viewModel));

    /// <summary>
    /// Shows or hides every widget's window at once, for the tray's hide-the-widgets
    /// command and the desktop's own double-click. Only the surfaces move: the
    /// widgets stay running.
    /// </summary>
    /// <remarks>
    /// Hiding is deliberate rather than closed, so nothing is created or destroyed and a
    /// widget keeps whatever it was showing. The windows are told by the desktop layer
    /// rather than by the window itself, because a hidden widget window must stay out of
    /// the way of the desktop's own show-desktop handling.
    /// <para>
    /// When the stored preference asks for it, the surfaces slide to the edge of their
    /// screen and back instead of appearing and disappearing. The state has already
    /// changed by the time this returns; the animation is only what the change looks
    /// like.
    /// </para>
    /// </remarks>
    public void SetVisible(bool visible, IReadOnlyList<ScreenBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        foreach (var widget in _widgets)
            widget.IsVisible = visible;

        _animation.SetVisible(_widgets, visible, screens, workspace.State.Settings);
    }

    public void Dispose()
    {
        _animation.Dispose();

        foreach (var widget in _widgets.ToArray())
            Destroy(widget);

        ticks.Dispose();
    }
}
