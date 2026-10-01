namespace DeskKit.Widgets.QuickLaunch;

/// <summary>
/// One entry in the launcher. Persisted as JSON inside the widget's settings, so
/// it must stay a plain data record.
/// </summary>
public sealed record QuickLaunchItem
{
    public string Name { get; init; } = string.Empty;

    /// <summary>A file, folder, "*.lnk" shortcut or URL.</summary>
    public string Target { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;
}
