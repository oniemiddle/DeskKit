namespace DeskKit.Core.Models;

/// <summary>The complete persisted state: preferences plus every widget instance.</summary>
public sealed record AppState
{
    /// <summary>Schema version of the file on disk.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public AppSettings Settings { get; init; } = new();

    public List<WidgetPlacement> Widgets { get; init; } = [];
}
