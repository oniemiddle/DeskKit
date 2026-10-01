namespace DeskKit.Core.Models;

/// <summary>Application wide preferences.</summary>
public sealed record AppSettings
{
    /// <summary>One of <c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; init; } = "System";

    public bool StartWithWindows { get; init; }

    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>Whether widgets are currently shown; toggled from the tray menu.</summary>
    public bool WidgetsVisible { get; init; } = true;
}
