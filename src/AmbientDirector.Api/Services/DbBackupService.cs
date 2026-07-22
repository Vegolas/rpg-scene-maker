using Microsoft.Data.Sqlite;

namespace AmbientDirector.Api.Services;

/// <summary>
/// Makes a consistent, self-contained copy of the SQLite database (issue #110) — the <c>ambient-director.db</c>
/// entry of the panel's full "download backup" zip (see <see cref="FullBackupService"/>). Uses SQLite's online
/// backup API (<see cref="SqliteConnection.BackupDatabase"/>) rather than a plain file copy so the snapshot is
/// safe against the live connection even in WAL mode: a raw copy of the <c>.db</c> alone can miss committed
/// pages still sitting in the <c>-wal</c> sidecar. The result is a single file with no sidecars.
///
/// This is the metadata database only (scenes, sounds/music metadata, settings, party, …); the on-disk
/// audio/image/locale files live outside the DB and are bundled separately by <see cref="FullBackupService"/>.
/// </summary>
public sealed class DbBackupService(string databasePath)
{
    public string DatabasePath => databasePath;

    /// <summary>Write a fresh, consistent snapshot of the live database to <paramref name="destinationPath"/>.</summary>
    public void BackupTo(string destinationPath)
    {
        // Pooling=False on both: these are one-shot connections, and — crucially for the destination —
        // a pooled connection keeps the file's OS handle open after Dispose, which would block the caller
        // from streaming (and deleting) the temp snapshot. Disabling pooling releases the handle on Dispose.
        using var source = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite;Pooling=False");
        source.Open();
        // A fresh, empty destination; BackupDatabase copies every page into it.
        using var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        destination.Open();
        source.BackupDatabase(destination);
    }
}
