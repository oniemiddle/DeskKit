using DeskKit.Core.Models;
using DeskKit.Core.Services;

namespace DeskKit.Core.Abstractions;

/// <summary>
/// The application's persisted state, read and written as a whole.
/// </summary>
/// <remarks>
/// The whole <see cref="AppState"/> moves at once because that is what the store has
/// always written: preferences and every placed widget are one document, and a partial
/// write is not a state anything else in the application knows how to read.
/// <para>
/// This is the only way the runtime sees storage. The implementation lives in
/// <c>DeskKit.Persistence</c>, which is the only project that holds a database stack,
/// so nothing above it is bound to EF Core or SQLite.
/// </para>
/// </remarks>
public interface IStateStore
{
    /// <summary>Where the state is kept. For the log, and for a diagnostic naming it.</summary>
    string DatabasePath { get; }

    /// <summary>
    /// A whole copy of the database taken before a schema migration runs, beside the
    /// database rather than at a fixed location, so a test backs up its own file and
    /// nobody else's.
    /// </summary>
    string PreMigrationBackupPath { get; }

    /// <summary>What the last <see cref="Load"/> found.</summary>
    StoreLoadReport LoadReport { get; }

    /// <summary>
    /// True when the store found state to load - a database, or the old configuration
    /// files it imported. Used to tell a first run apart from a user who deliberately
    /// removed every widget, so a default widget is seeded exactly once.
    /// </summary>
    bool HasStoredState { get; }

    /// <summary>
    /// Reads the state. A database that cannot be read, or that belongs to a newer
    /// build, is reported through <see cref="LoadReport"/> and yields empty state
    /// rather than throwing; see <see cref="StoreLoadReport.IsReadOnly"/>.
    /// </summary>
    AppState Load();

    /// <summary>
    /// Writes the state, unless the session may not write. A failure to write throws,
    /// so the caller logs the reason.
    /// </summary>
    StoreSaveReport Save(AppState state);
}
