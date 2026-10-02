using Avalonia.Threading;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using Microsoft.Extensions.Logging;

namespace DeskKit.App.Shell;

/// <summary>
/// Owns the application state in memory and is the only thing that asks the store to
/// write it.
/// </summary>
/// <remarks>
/// The store is the single writer on disk; this is the single owner in memory. It does
/// nothing else on purpose: the rules that decide what the state should say live in
/// <c>DeskKit.Core</c>, and the things that react to it - windows, the tray, the
/// settings window - live somewhere else again. Keeping it to ownership and writing is
/// what stops it becoming the class it was extracted from.
/// <para>
/// Writes are debounced. Dragging a widget changes the state once per pixel, so writing
/// on every change would hammer the disk for the length of the drag.
/// </para>
/// </remarks>
internal sealed class WorkspaceState(IStateStore store, ILogger logger) : IDisposable
{
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    private DispatcherTimer? _saveTimer;

    public AppState State { get; private set; } = new();

    /// <summary>What the last load found, so the session can explain itself.</summary>
    public StoreLoadReport LoadReport => store.LoadReport;

    /// <summary>True when the store found state to load, which is what tells a first run apart.</summary>
    public bool HasStoredState => store.HasStoredState;

    /// <summary>Where the database is, for the log and for a diagnostic naming it.</summary>
    public string DatabasePath => store.DatabasePath;

    /// <summary>Raised after a change the open UI has to react to.</summary>
    public event EventHandler? Changed;

    public AppState Load()
    {
        State = store.Load();
        return State;
    }

    public void ReplaceSettings(AppSettings settings)
    {
        State = State with { Settings = settings };
        ScheduleSave();
    }

    /// <summary>
    /// Replaces the whole widget list, which is how a settings migration reports itself:
    /// the migration already produced the placements it wants stored.
    /// </summary>
    public void ReplaceWidgets(IReadOnlyList<WidgetPlacement> widgets)
    {
        State = State with { Widgets = [.. widgets] };
        ScheduleSave();
    }

    public void AddWidgets(IReadOnlyList<WidgetPlacement> placements)
    {
        if (placements.Count == 0)
            return;

        State.Widgets.AddRange(placements);
        ScheduleSave();
    }

    public void AddWidget(WidgetPlacement placement)
    {
        State.Widgets.Add(placement);
        ScheduleSave();
    }

    public void RemoveWidget(string instanceId)
    {
        State.Widgets.RemoveAll(placement => placement.InstanceId == instanceId);
        ScheduleSave();
    }

    /// <summary>
    /// Copies a widget's live position and size back into the stored placement.
    /// </summary>
    public void SetPlacement(WidgetPlacement placement)
    {
        var index = State.Widgets.FindIndex(
            stored => stored.InstanceId == placement.InstanceId);

        if (index < 0)
            return;

        State.Widgets[index] = placement;
        ScheduleSave();
    }

    /// <summary>Tells open UI that the state changed in a way it has to redraw.</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Coalesces bursts of changes into one write.</summary>
    public void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = SaveDebounce };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer!.Stop();
                SaveNow();
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try
        {
            // A refusal is deliberately not reported here: the session already said what
            // may not be written, in full, and this runs on every debounced change.
            // Repeating it would only fill the log.
            store.Save(State);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the configuration");
        }
    }

    public void Dispose()
    {
        _saveTimer?.Stop();
        _saveTimer = null;
        SaveNow();
    }
}
