using System.Text.Json.Serialization;

namespace DeskKit.Widgets.TickTick;

/// <summary>A task as returned by TickTick's Open API.</summary>
public sealed class TickTickTask
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    [JsonIgnore]
    public string ListName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    [JsonPropertyName("desc")]
    public string? Description { get; set; }
    public bool IsAllDay { get; set; }
    public string? StartDate { get; set; }
    public string? DueDate { get; set; }
    public string? TimeZone { get; set; }
    public string? RepeatFlag { get; set; }
    public List<string>? Reminders { get; set; }
    public int Priority { get; set; }
    public int Status { get; set; }
    public string? CompletedTime { get; set; }
    public long SortOrder { get; set; }
    public List<TickTickChecklistItem>? Items { get; set; }
}

public sealed class TickTickChecklistItem
{
    public string? Id { get; set; }
    public int Status { get; set; }
    public string? Title { get; set; }
    public long SortOrder { get; set; }
    public string? StartDate { get; set; }
    public bool IsAllDay { get; set; }
    public string? TimeZone { get; set; }
    public string? CompletedTime { get; set; }
}

public sealed record TickTickProject(string Id, string Name, bool Closed = false, string? Kind = null);

public sealed class TickTickProjectData
{
    public List<TickTickTask> Tasks { get; set; } = [];
}
