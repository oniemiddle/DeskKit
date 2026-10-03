namespace DeskKit.Core.Models;

/// <summary>Application wide preferences.</summary>
public sealed record AppSettings
{
    /// <summary>One of <c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; init; } = ThemeSetting.System;

    /// <summary>
    /// The UI language: <c>System</c> to follow the machine, or a culture name such
    /// as <c>zh-Hans</c>. See <see cref="LanguageSetting"/>.
    /// </summary>
    public string Language { get; init; } = LanguageSetting.System;

    public bool StartWithWindows { get; init; }

    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>Whether widgets are currently shown; toggled from the tray menu.</summary>
    public bool WidgetsVisible { get; init; } = true;

    /// <summary>
    /// Whether double-clicking an empty part of the desktop shows or hides every
    /// widget, the way the tray's command does.
    /// </summary>
    /// <remarks>
    /// Off by default: the gesture is one Explorer offers for its own purposes
    /// ("double-click to show desktop icons"), so it is the user's to turn on.
    /// </remarks>
    public bool DesktopDoubleClickTogglesWidgets { get; init; }

    /// <summary>
    /// Whether showing or hiding every widget is animated. See
    /// <see cref="WidgetAnimationSetting"/>, which also names the only animation
    /// there is.
    /// </summary>
    public string WidgetsAnimation { get; init; } = WidgetAnimationSetting.Slide;

    /// <summary>How long a slide takes. See <see cref="WidgetAnimationSpeedSetting"/>.</summary>
    public string WidgetAnimationSpeed { get; init; } = WidgetAnimationSpeedSetting.Standard;

    /// <summary>Which side the widgets leave by. See <see cref="WidgetAnimationDirectionSetting"/>.</summary>
    public string WidgetAnimationDirection { get; init; } = WidgetAnimationDirectionSetting.Right;

    /// <summary>How hard a slide decelerates. See <see cref="WidgetAnimationEasingSetting"/>.</summary>
    public string WidgetAnimationEasing { get; init; } = WidgetAnimationEasingSetting.Standard;
}
