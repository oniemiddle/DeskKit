using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DeskKit.Core.Data;

/// <summary>
/// Builds the options every connection to the store is opened with, so the shell,
/// the design-time tools and the tests cannot drift apart.
/// </summary>
public static class DatabaseOptions
{
    /// <summary>
    /// How long a statement waits for another connection's lock before giving up.
    /// The equivalent of the retries the file store needed for the moment an
    /// antivirus scan held a file open.
    /// </summary>
    public const int BusyTimeoutMs = 3000;

    public static string ConnectionStringFor(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public static DbContextOptions<DeskKitDbContext> For(string databasePath) =>
        new DbContextOptionsBuilder<DeskKitDbContext>()
            .UseSqlite(ConnectionStringFor(databasePath))
            .AddInterceptors(new SqliteConnectionPragmas())
            .Options;
}
