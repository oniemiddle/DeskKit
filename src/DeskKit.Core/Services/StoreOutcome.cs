using DeskKit.Core.Abstractions;

namespace DeskKit.Core.Services;

/// <summary>What happened the last time the store was opened.</summary>
public enum StoreOutcome
{
    /// <summary>Nothing has been loaded yet.</summary>
    NotLoaded,

    /// <summary>There was no database and nothing to import: a first run.</summary>
    FirstRun,

    /// <summary>An existing database was read.</summary>
    Loaded,

    /// <summary>The old JSON configuration was read into a new database.</summary>
    Imported,

    /// <summary>
    /// The database was written by a newer build. It is not migrated and never
    /// written over, because folding it back to this build's schema would hide from
    /// the newer build that the data has already moved on.
    /// </summary>
    NewerSchema,

    /// <summary>
    /// The database could not be opened or could not be brought up to this build's
    /// schema. Nothing was read and nothing may be written, because writing over it
    /// would destroy data that was never seen.
    /// </summary>
    Unavailable,
}

/// <summary>One thing the store could not read, and where it was.</summary>
/// <param name="Subject">The row or file it belongs to, for the log.</param>
/// <param name="Detail">What was wrong, for the log.</param>
public sealed record StoreProblem(string Subject, string Detail);

/// <summary>What <see cref="IStateStore.Load"/> found.</summary>
/// <param name="ImportedFrom">
/// Which of the old configuration files the state came from, when it was imported.
/// </param>
public sealed record StoreLoadReport(
    StoreOutcome Outcome,
    IReadOnlyList<StoreProblem> Problems,
    string? ImportedFrom)
{
    /// <summary>
    /// True when something was lost or deliberately left alone, which is the point at
    /// which the user should be told rather than only the log.
    /// </summary>
    public bool HasProblems =>
        Outcome is StoreOutcome.NewerSchema or StoreOutcome.Unavailable
        || Problems.Count > 0;

    /// <summary>
    /// True when the store may not be written to this session: either the database
    /// belongs to a newer build, or it could not be read at all.
    /// </summary>
    public bool IsReadOnly => Outcome is StoreOutcome.NewerSchema or StoreOutcome.Unavailable;
}

/// <summary>What happened when the store was saved.</summary>
public enum StoreSaveOutcome
{
    Saved,

    /// <summary>Nothing was written, because the database could not be read.</summary>
    RefusedUnavailable,

    /// <summary>Nothing was written, because the database belongs to a newer build.</summary>
    RefusedNewerSchema,
}

/// <summary>What <see cref="IStateStore.Save"/> wrote.</summary>
/// <param name="RowsWritten">
/// How many rows the database actually changed. Zero means the state was already
/// stored, which is what keeps a debounced save after a drag from rewriting rows
/// that did not move.
/// </param>
public sealed record StoreSaveReport(StoreSaveOutcome Outcome, int RowsWritten);
