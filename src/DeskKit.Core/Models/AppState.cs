namespace DeskKit.Core.Models;

/// <summary>The complete persisted state: preferences plus every widget instance.</summary>
/// <remarks>
/// There is no version number here any more. The shape of what is stored is the
/// database schema, and its version is the set of EF Core migrations recorded in the
/// database itself; a number in the state would be a second answer to a question
/// that already has one.
/// </remarks>
public sealed record AppState
{
    public AppSettings Settings { get; init; } = new();

    public List<WidgetPlacement> Widgets { get; init; } = [];
}
