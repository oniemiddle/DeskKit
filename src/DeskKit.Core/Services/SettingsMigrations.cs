using DeskKit.Core.Models;

namespace DeskKit.Core.Services;

/// <summary>Runs a widget's own settings migrations, without knowing what is in them.</summary>
public static class SettingsMigrations
{
    /// <summary>
    /// Brings <paramref name="settings"/> from <paramref name="fromVersion"/> up to
    /// <paramref name="targetVersion"/>.
    /// </summary>
    /// <param name="problem">
    /// Why nothing was done, or null when the settings are at the target version.
    /// </param>
    /// <returns>
    /// The version the settings are now at. This is <paramref name="fromVersion"/>
    /// whenever anything was left undone, so the next start tries again instead of
    /// believing a migration that never happened.
    /// </returns>
    public static int Apply(
        WidgetSettings settings,
        int fromVersion,
        int targetVersion,
        IReadOnlyList<WidgetSettingsMigration> steps,
        out string? problem)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(steps);

        problem = null;

        if (fromVersion == targetVersion)
            return fromVersion;

        if (fromVersion > targetVersion)
        {
            // This widget was written by a newer build. Its settings are not ours to
            // rearrange, and stamping our number on them would tell the newer build
            // they had already moved on when they had not.
            problem = $"the stored settings are version {fromVersion} but this build writes version {targetVersion}";
            return fromVersion;
        }

        var byVersion = new Dictionary<int, WidgetSettingsMigration>();
        foreach (var step in steps)
        {
            if (!byVersion.TryAdd(step.FromVersion, step))
            {
                problem = $"more than one migration starts at settings version {step.FromVersion}";
                return fromVersion;
            }
        }

        // The whole path is checked before any of it runs. A step that has already
        // rewritten the settings cannot be rolled back, so a gap halfway along would
        // leave them part old and part new, and the retry would migrate twice.
        for (var version = fromVersion; version < targetVersion; version++)
        {
            if (!byVersion.ContainsKey(version))
            {
                problem = $"there is no migration from settings version {version}";
                return fromVersion;
            }
        }

        for (var version = fromVersion; version < targetVersion; version++)
            byVersion[version].Apply(settings);

        return targetVersion;
    }
}
