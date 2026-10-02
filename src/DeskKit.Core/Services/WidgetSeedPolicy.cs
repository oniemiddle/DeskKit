using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;

namespace DeskKit.Core.Services;

/// <summary>
/// The placement rules for a widget that is being put on the desktop for the first
/// time: where it lands, how big it starts, and which settings version it is stamped
/// with.
/// </summary>
/// <remarks>
/// The position is derived from the widget's own defaults and how many are already
/// placed, so several widgets added at once cascade instead of stacking exactly on top
/// of each other. Pure, so the cascade is unit-tested rather than discovered by
/// clicking "add" six times.
/// </remarks>
public static class WidgetSeedPolicy
{
    /// <summary>Distance between two widgets that are created in the same run.</summary>
    public const int CascadeStep = 32;

    /// <summary>How far a widget is placed from the screen's own origin.</summary>
    public const int InitialOffset = 80;

    /// <summary>How many cascade steps there are before the pattern repeats.</summary>
    public const int CascadeCycle = 6;

    /// <summary>
    /// Builds the placement for one widget. <paramref name="existingCount"/> is how many
    /// widgets are already placed, which is what decides the cascade.
    /// </summary>
    public static WidgetPlacement CreatePlacement(
        IWidgetProvider provider,
        int existingCount,
        IReadOnlyList<ScreenBounds> screens,
        string instanceId)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(instanceId);

        var descriptor = provider.Descriptor;
        var screen = screens.Count > 0 ? screens[0] : default;
        var offset = (existingCount % CascadeCycle) * CascadeStep;

        return new WidgetPlacement
        {
            InstanceId = instanceId,
            WidgetId = descriptor.Id,
            X = screen.X + InitialOffset + offset,
            Y = screen.Y + InitialOffset + offset,
            Width = descriptor.DefaultWidth,
            Height = descriptor.DefaultHeight,

            // A widget created now is written in today's shape, so it never has to be
            // migrated and must not be stamped with a version it did not come from.
            SettingsVersion = (provider as IWidgetSettingsMigrations)?.SettingsVersion ?? 1,
        };
    }

    /// <summary>
    /// The placements a first run should create, in the order the caller asked for
    /// them. A widget whose type is not registered is skipped rather than throwing:
    /// a layout naming a widget this build does not have must not stop the others from
    /// appearing.
    /// </summary>
    /// <param name="widgetIds">
    /// The widget types the first run should show. Which ones those are is a product
    /// decision, not a property of a widget.
    /// </param>
    /// <param name="registry">The widget types this build knows about.</param>
    /// <param name="screens">The current monitor layout, in physical pixels.</param>
    /// <param name="newInstanceId">Supplies a fresh identity for each placement.</param>
    public static IReadOnlyList<WidgetPlacement> CreateFirstRunPlacements(
        IReadOnlyList<string> widgetIds,
        WidgetRegistry registry,
        IReadOnlyList<ScreenBounds> screens,
        Func<string> newInstanceId)
    {
        ArgumentNullException.ThrowIfNull(widgetIds);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(newInstanceId);

        var placements = new List<WidgetPlacement>(widgetIds.Count);

        foreach (var widgetId in widgetIds)
        {
            if (registry.Find(widgetId) is not { } provider)
                continue;

            placements.Add(CreatePlacement(provider, placements.Count, screens, newInstanceId()));
        }

        return placements;
    }
}
