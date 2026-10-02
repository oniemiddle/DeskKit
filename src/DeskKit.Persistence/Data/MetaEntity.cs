namespace DeskKit.Persistence.Data;

/// <summary>
/// One fact about the database itself, as a row of the <c>Meta</c> table.
/// </summary>
/// <remarks>
/// Diagnostics rather than state: it records where the old configuration files were
/// read from, so "why is my layout the one from the other machine" has an answer
/// on disk. Nothing in the shell reads it to decide anything.
/// </remarks>
public sealed class MetaEntity
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
