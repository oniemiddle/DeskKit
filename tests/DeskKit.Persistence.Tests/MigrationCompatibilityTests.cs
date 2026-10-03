using DeskKit.Core.Services;
using DeskKit.Persistence.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DeskKit.Persistence.Tests;

/// <summary>
/// The database a build understands is decided by comparing the migration ids compiled
/// into it with the ids recorded in <c>__EFMigrationsHistory</c>. Those ids are names
/// like <c>20261002062527_InitialCreate</c> - they do not contain the namespace the
/// migration class happens to live in.
/// <para>
/// That is what makes it safe to move the migrations to another assembly and namespace:
/// a database written by an earlier build still looks fully migrated rather than
/// looking as though it needs migrating, or worse, as though it came from a newer build.
/// These tests pin that property, because getting it wrong would either re-run
/// migrations over a user's data or refuse to open the database at all.
/// </para>
/// </summary>
public sealed class MigrationCompatibilityTests : IDisposable
{
    /// <summary>The id an existing installation already has recorded.</summary>
    private const string InitialCreateId = "20261002062527_InitialCreate";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "deskkit-migrations-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, AppPaths.DatabaseFileName);

    public MigrationCompatibilityTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TheMigrationIdsDoNotCarryTheirNamespace()
    {
        using var context = new DeskKitDbContext(DatabaseOptions.For(DatabasePath));

        var migrations = context.Database.GetMigrations().ToList();

        // A database written by an earlier build recorded exactly this id, namespace
        // and all, so this one must still be among the ids this build understands.
        Assert.Contains(InitialCreateId, migrations);

        // Every id, not just the first: a migration added later has to keep the same
        // property, or a database written by an earlier build stops being recognised.
        foreach (var migration in migrations)
        {
            Assert.DoesNotContain("DeskKit.", migration, StringComparison.Ordinal);
            Assert.DoesNotContain("Core", migration, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ANewDatabaseRecordsThatIdAndHasNothingPending()
    {
        var store = new StateStore(DatabasePath);
        store.Load();

        using var context = new DeskKitDbContext(DatabaseOptions.For(DatabasePath));

        Assert.Equal(
            context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal),
            context.Database.GetAppliedMigrations().OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.Equal(InitialCreateId, ReadRecordedMigrationId());
    }

    [Fact]
    public void ADatabaseWrittenByAnEarlierBuildIsBroughtUpToDateRatherThanRefused()
    {
        // The database as the build that only knew the first migration left it: a real
        // file, with the real schema and the real history row for that one migration,
        // which is the whole of what a database says about where it came from. A
        // migration added since is one this build is expected to apply, which is a
        // different thing from a file it does not understand.
        using (var context = new DeskKitDbContext(DatabaseOptions.For(DatabasePath)))
            context.Database.GetService<IMigrator>().Migrate(InitialCreateId);

        var store = new StateStore(DatabasePath);
        store.Load();

        Assert.NotEqual(StoreOutcome.NewerSchema, store.LoadReport.Outcome);
        Assert.NotEqual(StoreOutcome.Unavailable, store.LoadReport.Outcome);
        Assert.False(store.LoadReport.IsReadOnly);

        using var reloaded = new DeskKitDbContext(DatabaseOptions.For(DatabasePath));
        Assert.Empty(reloaded.Database.GetPendingMigrations());
    }

    [Fact]
    public void ADatabaseWithAnUnknownMigrationIsRefusedRatherThanWrittenOver()
    {
        // The other half of the rule: an id this build has never heard of means the file
        // was written by a newer build, and it must be left exactly as it is.
        WriteHistoryRow("20990101000000_FromTheFuture");

        var store = new StateStore(DatabasePath);
        store.Load();

        Assert.Equal(StoreOutcome.NewerSchema, store.LoadReport.Outcome);
        Assert.True(store.LoadReport.IsReadOnly);
    }

    private void WriteHistoryRow(string migrationId)
    {
        using (var connection = new SqliteConnection(DatabaseOptions.ConnectionStringFor(DatabasePath)))
        {
            connection.Open();

            // The application always leaves the file in write-ahead logging, so a
            // database written by an earlier build is in that mode too. Creating one in
            // the default rollback-journal mode instead would make the next open fail
            // for a reason that has nothing to do with migration ids.
            using (var mode = connection.CreateCommand())
            {
                mode.CommandText = "PRAGMA journal_mode=WAL;";
                mode.ExecuteNonQuery();
            }

            using (var create = connection.CreateCommand())
            {
                create.CommandText =
                    "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" ("
                    + "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, "
                    + "\"ProductVersion\" TEXT NOT NULL);";
                create.ExecuteNonQuery();
            }

            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, '10.0.12');";
            insert.Parameters.AddWithValue("$id", migrationId);
            insert.ExecuteNonQuery();
        }

        // The provider pools connections, so a handle from this writer would otherwise
        // still be open when the store opens its own.
        SqliteConnection.ClearAllPools();
    }

    private string? ReadRecordedMigrationId()
    {
        using var connection = new SqliteConnection(DatabaseOptions.ConnectionStringFor(DatabasePath));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" LIMIT 1;";
        return command.ExecuteScalar() as string;
    }
}
