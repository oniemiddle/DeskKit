using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeskKit.Persistence.Data;

/// <summary>
/// Puts the database into write-ahead logging and asks it to flush on commit.
/// </summary>
/// <remarks>
/// The journal mode is a property of the file, so setting it is a no-op after the
/// first time; the other two are per connection, which is why they are applied
/// whenever a connection is opened instead of once at startup. Asking for a commit
/// that reaches the disk is the whole reason for using a database here: it is what
/// replaces writing a temporary file and renaming it.
/// </remarks>
internal sealed class SqliteConnectionPragmas : DbConnectionInterceptor
{
    private static readonly string Statement =
        "PRAGMA journal_mode=WAL; "
        + "PRAGMA synchronous=FULL; "
        + "PRAGMA busy_timeout=" + DatabaseOptions.BusyTimeoutMs + ";";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Apply(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Statement;
        command.ExecuteNonQuery();
    }
}
