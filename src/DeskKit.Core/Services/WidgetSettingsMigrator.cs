using DeskKit.Core.Models;

namespace DeskKit.Core.Services;

/// <summary>
/// What one migration pass did to one stored widget. Entries are returned in the
/// order the placements were given, so a caller can log them in the same order it
/// would have discovered them.
/// </summary>
/// <param name="InstanceId">Which placed widget this is about.</param>
/// <param name="WidgetId">Its type.</param>
/// <param name="Placement">The placement to store: rewritten when the version moved, otherwise unchanged.</param>
/// <param name="Version">The version the settings are now at.</param>
/// <param name="Migrated">True when the version moved.</param>
/// <param name="Problem">
/// Why nothing was done, or null when there was nothing to do or the work succeeded.
/// </param>
public sealed record WidgetMigrationEntry(
    string InstanceId,
    string WidgetId,
    WidgetPlacement Placement,
    int Version,
    bool Migrated,
    string? Problem);

/// <summary>What one migration pass did to a set of stored placements.</summary>
public sealed record WidgetMigrationResult(IReadOnlyList<WidgetMigrationEntry> Entries)
{
    /// <summary>The placements to store, in their original order.</summary>
    public IReadOnlyList<WidgetPlacement> Placements => [.. Entries.Select(entry => entry.Placement)];

    /// <summary>
    /// True when at least one version moved, which is the only reason a migration pass
    /// needs its result written to disk.
    /// </summary>
    public bool Changed => Entries.Any(entry => entry.Migrated);
}

/// <summary>
/// Brings each placed widget's own settings up to the version its provider declares.
/// </summary>
/// <remarks>
/// The shell carries <see cref="WidgetPlacement.SettingsVersion"/> around without
/// interpreting it, so this walks the placements, asks each widget's provider how far
/// its own settings have come, and runs that provider's steps through
/// <see cref="SettingsMigrations.Apply"/>. Nothing here looks inside a widget's
/// settings.
/// <para>
/// Nothing is written back by this type: it returns the placements to store and the
/// caller decides when to save, because a migration that only reached the disk on exit
/// would run again on every start until then.
/// </para>
/// </remarks>
public static class WidgetSettingsMigrator
{
    public static WidgetMigrationResult Apply(
        IReadOnlyList<WidgetPlacement> placements,
        WidgetRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(registry);

        var entries = new List<WidgetMigrationEntry>(placements.Count);

        foreach (var placement in placements)
        {
            // A widget that does not declare a version has nothing to migrate, which is
            // the same as declaring version 1 with no steps.
            if (registry.Find(placement.WidgetId) is not IWidgetSettingsMigrations declared)
            {
                entries.Add(Unchanged(placement));
                continue;
            }

            // The settings dictionary is handed over by reference, so a step that
            // rewrites it rewrites the one the placement holds - the same object the
            // widget is given when its window is created.
            var version = SettingsMigrations.Apply(
                new WidgetSettings(placement.Settings),
                placement.SettingsVersion,
                declared.SettingsVersion,
                declared.Migrations,
                out var problem);

            if (problem is not null)
            {
                entries.Add(new WidgetMigrationEntry(
                    placement.InstanceId,
                    placement.WidgetId,
                    placement,
                    placement.SettingsVersion,
                    Migrated: false,
                    Problem: problem));
                continue;
            }

            if (version == placement.SettingsVersion)
            {
                entries.Add(Unchanged(placement));
                continue;
            }

            entries.Add(new WidgetMigrationEntry(
                placement.InstanceId,
                placement.WidgetId,
                placement with { SettingsVersion = version },
                version,
                Migrated: true,
                Problem: null));
        }

        return new WidgetMigrationResult(entries);
    }

    private static WidgetMigrationEntry Unchanged(WidgetPlacement placement) =>
        new(placement.InstanceId, placement.WidgetId, placement, placement.SettingsVersion, false, null);
}
