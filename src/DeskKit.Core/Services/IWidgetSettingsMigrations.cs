using DeskKit.Core.Models;

namespace DeskKit.Core.Services;

/// <summary>
/// The shape a widget's settings are in, and how to bring older ones forward.
/// </summary>
/// <remarks>
/// Implemented by a widget's provider, so the widget owns its own history. The
/// shell carries <see cref="WidgetPlacement.SettingsVersion"/> around without ever
/// interpreting it and runs these steps in order without looking inside them, which
/// is what keeps the settings an opaque dictionary to everything but their widget.
/// <para>
/// Not implementing this means "version 1, nothing to migrate".
/// </para>
/// </remarks>
public interface IWidgetSettingsMigrations
{
    /// <summary>The version the widget writes today.</summary>
    int SettingsVersion { get; }

    /// <summary>
    /// One step per version, each producing the next. A missing step is treated as
    /// an unfinished migration rather than an empty one.
    /// </summary>
    IReadOnlyList<WidgetSettingsMigration> Migrations { get; }
}
