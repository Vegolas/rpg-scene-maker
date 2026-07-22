using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// GET /setup/backup (issues #110, #153) streams a full backup as one zip: a consistent SQLite snapshot at the
/// zip root (ambient-director.db) plus the on-disk sounds/music/images/locales folders, mirroring
/// %LocalAppData%\AmbientDirector\ so restoring is "stop the app and unzip over that folder". It comes back as
/// an attachment, the db entry must be a real openable database, and a file in a data dir must show up under
/// its canonical folder.
/// </summary>
[Collection("integration")]
public class BackupTests
{
    // Every SQLite database file begins with this 16-byte header string.
    private static readonly byte[] SqliteMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");

    [Fact]
    public async Task Backup_returns_a_zip_with_a_valid_sqlite_db_at_the_root()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/setup/backup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Downloaded as an attachment with a timestamped .zip filename so the browser saves it.
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        Assert.NotNull(fileName);
        Assert.StartsWith("ambient-director-backup-", fileName);
        Assert.EndsWith(".zip", fileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        // The database snapshot sits at the zip root under the canonical name.
        var dbEntry = archive.GetEntry("ambient-director.db");
        Assert.NotNull(dbEntry);

        // Prove it's a usable database: extract it and open it — the migrated schema is present.
        var path = Path.Combine(Path.GetTempPath(), "ad-backup-test", Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using (var entryStream = dbEntry!.Open())
            await using (var file = File.Create(path))
            {
                await entryStream.CopyToAsync(file);
            }

            var dbBytes = await File.ReadAllBytesAsync(path);
            Assert.True(dbBytes.Length > SqliteMagic.Length);
            Assert.Equal(SqliteMagic, dbBytes[..SqliteMagic.Length]);

            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Scenes';";
            Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Backup_bundles_data_folder_files_under_their_canonical_folder()
    {
        using var factory = new ApiFactory();
        // Boot the app first so the storage services create their data dirs, then drop a file into sounds/.
        var client = factory.CreateClient();

        Directory.CreateDirectory(factory.SoundsPath);
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(Path.Combine(factory.SoundsPath, "dummy-sound.mp3"), payload);

        var response = await client.GetAsync("/setup/backup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        // The file lands under the canonical "sounds/" folder with a forward-slash entry name, contents intact.
        var soundEntry = archive.GetEntry("sounds/dummy-sound.mp3");
        Assert.NotNull(soundEntry);
        using var entryStream = soundEntry!.Open();
        using var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        Assert.Equal(payload, ms.ToArray());
    }
}
