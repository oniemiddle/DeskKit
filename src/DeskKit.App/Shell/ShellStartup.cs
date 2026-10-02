using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using Microsoft.Extensions.Logging;
using DeskKit.Runtime;

namespace DeskKit.App.Shell;

/// <summary>
/// The once-per-session work that explains what the store held and brings it in line
/// with the machine it is being read on.
/// </summary>
/// <remarks>
/// Both jobs here are product policy rather than runtime behaviour: what a load report
/// is worth saying out loud, and how a widget's stored settings catch up with the
/// version its provider declares. The runtime only ever reacts to the state it is
/// handed, so it has no opinion about either.
/// </remarks>
internal sealed class ShellStartup(WidgetRegistry registry, ILogger logger)
{
    /// <summary>
    /// Says what the load found. A plain load says nothing: a session that worked is
    /// not worth a line.
    /// </summary>
    /// <remarks>
    /// The two messages that explain a session are logged here rather than when a save
    /// is refused, because this runs before anything can be changed and a session may
    /// end without ever attempting to save, which would leave the user with an empty
    /// desktop and no explanation anywhere.
    /// </remarks>
    public void ReportLoad(StoreLoadReport report, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(report);

        foreach (var problem in report.Problems)
        {
            logger.LogWarning(
                "Storage problem with {Subject}: {Detail}", problem.Subject, problem.Detail);
        }

        switch (report.Outcome)
        {
            case StoreOutcome.NewerSchema:
                logger.LogWarning(
                    "The database {File} was written by a newer version of DeskKit, so it was not "
                    + "read and will not be written over. Upgrade DeskKit to use the layout it "
                    + "holds; nothing done in this session will be saved.",
                    databasePath);
                break;

            case StoreOutcome.Unavailable:
                logger.LogWarning(
                    "The database {File} could not be opened, so DeskKit started with nothing "
                    + "loaded and will not write over it. Nothing done in this session will be "
                    + "saved. Close whatever is holding it, then restart DeskKit.",
                    databasePath);
                break;

            case StoreOutcome.Imported:
                logger.LogInformation(
                    "Imported the configuration from {Source} into {Database}",
                    report.ImportedFrom,
                    databasePath);
                break;
        }
    }

    /// <summary>
    /// The widgets a first run should put on the desktop, in the order the layout names
    /// them, cascaded into a place each can be seen.
    /// </summary>
    /// <remarks>
    /// Which widgets those are is a product decision rather than a property of a widget,
    /// so it is read from <see cref="DefaultLayout"/> and never from a descriptor: a
    /// widget cannot declare that it must appear the first time the application runs.
    /// A layout naming a widget this build does not have is skipped rather than fatal.
    /// </remarks>
    public IReadOnlyList<WidgetPlacement> FirstRunPlacements(IReadOnlyList<ScreenBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        return WidgetSeedPolicy.CreateFirstRunPlacements(
            DefaultLayout.WidgetIds,
            registry,
            screens,
            () => Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Brings each widget's own settings up to the version its provider declares, and
    /// answers with the placements to store, or null when nothing had to change.
    /// </summary>
    /// <remarks>
    /// This does write back into the stored placements, which the placement rules
    /// otherwise never do. The difference is what is being written: a display that
    /// cannot show a widget is this session's problem, while a settings migration is a
    /// real change to what was stored, the same kind of correction as reconciling the
    /// start-with-Windows flag against the registry.
    /// </remarks>
    public IReadOnlyList<WidgetPlacement>? MigrateWidgetSettings(
        IReadOnlyList<WidgetPlacement> widgets)
    {
        ArgumentNullException.ThrowIfNull(widgets);

        var result = WidgetSettingsMigrator.Apply(widgets, registry);

        // Logged in the order the placements were read, so the log of a session with
        // several widgets reads the same way it did before the walk moved out of the shell.
        foreach (var entry in result.Entries)
        {
            if (entry.Problem is not null)
            {
                logger.LogWarning(
                    "Widget {InstanceId} ({WidgetId}) settings were left alone: {Problem}",
                    entry.InstanceId,
                    entry.WidgetId,
                    entry.Problem);
                continue;
            }

            if (entry.Migrated)
            {
                logger.LogInformation(
                    "Widget {InstanceId} ({WidgetId}) settings brought forward to version {Version}",
                    entry.InstanceId,
                    entry.WidgetId,
                    entry.Version);
            }
        }

        return result.Changed ? result.Placements : null;
    }
}