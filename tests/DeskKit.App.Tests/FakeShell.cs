using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Runtime;

namespace DeskKit.App.Tests;

/// <summary>
/// The shell as the product's own UI uses it: the state it reads and the two things it may
/// do — store settings, and run the show/hide command.
/// </summary>
internal sealed class FakeShell(AppSettings settings) : IShellFacade
{
    public List<bool> VisibilityChanges { get; } = [];

    public AppState State { get; private set; } = new() { Settings = settings };

    public event EventHandler? StateChanged;

    /// <summary>Nothing in these tests listens, so they are never raised.</summary>
    public event EventHandler<WidgetRuntime>? WidgetAdded
    {
        add
        {
        }
        remove
        {
        }
    }

    public event EventHandler<WidgetViewModel>? SettingsRequested
    {
        add
        {
        }
        remove
        {
        }
    }

    public StoreLoadReport LoadReport => new(StoreOutcome.NotLoaded, [], null);

    public IReadOnlyList<WidgetRuntime> Runtimes => [];

    public IReadOnlyList<IWidgetProvider> AvailableWidgets => [];

    public WidgetRuntime? AddWidget(IWidgetProvider provider) => null;

    public void RemoveWidget(WidgetViewModel widget)
    {
    }

    public void ApplySettings(AppSettings newSettings)
    {
        State = State with { Settings = newSettings };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetWidgetsVisible(bool visible)
    {
        VisibilityChanges.Add(visible);
        State = State with { Settings = State.Settings with { WidgetsVisible = visible } };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
