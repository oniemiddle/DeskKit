using DeskKit.Core.Models;

namespace DeskKit.Persistence.Data;

/// <summary>
/// The application preferences, as the single row of the <c>Settings</c> table.
/// </summary>
/// <remarks>
/// One row rather than a key/value table, because the preferences are a known,
/// small set: adding one is a migration with a column in it, which is the part EF
/// Core does well, instead of a value nothing type-checks.
/// </remarks>
public sealed class SettingsEntity
{
    /// <summary>Always <see cref="SingletonId"/>: the table holds one row.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>One of <c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; set; } = ThemeSetting.System;

    /// <summary><c>System</c>, or a culture name such as <c>zh-Hans</c>.</summary>
    public string Language { get; set; } = LanguageSetting.System;

    public bool StartWithWindows { get; set; }

    public bool ShowTrayIcon { get; set; } = true;

    public bool WidgetsVisible { get; set; } = true;

    public bool DesktopDoubleClickTogglesWidgets { get; set; }

    public string WidgetsAnimation { get; set; } = WidgetAnimationSetting.Slide;

    public string WidgetAnimationSpeed { get; set; } = WidgetAnimationSpeedSetting.Standard;

    public string WidgetAnimationDirection { get; set; } = WidgetAnimationDirectionSetting.Right;

    public string WidgetAnimationEasing { get; set; } = WidgetAnimationEasingSetting.Standard;
}
