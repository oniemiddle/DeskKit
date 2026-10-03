using DeskKit.Persistence.Data;
using Microsoft.Data.Sqlite;

namespace DeskKit.Persistence;

/// <summary>Copies a database while it is in use, consistently.</summary>
internal static class DatabaseBackup
{
    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// Through SQLite's own backup API rather than <c>File.Copy</c>: in write-ahead
    /// logging the newest commits are still in the <c>-wal</c> file, so copying the
    /// database file on its own can produce a copy that is missing them —a backup
    /// that looks fine until the day it is needed. A same-named file is deleted
    /// first, because a backup left over from an earlier migration must not be merged
    /// with this one.
    /// </remarks>
    public static void Write(string sourcePath, string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        using var source = new SqliteConnection(DatabaseOptions.ConnectionStringFor(sourcePath));
        using var destination = new SqliteConnection(DatabaseOptions.ConnectionStringFor(destinationPath));

        source.Open();
        destination.Open();

        source.BackupDatabase(destination);
    }
}
