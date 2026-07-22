using System.IO.Compression;

namespace AmbientDirector.Api.Services;

/// <summary>
/// Builds the panel's "download backup" archive (issue #153): one <c>.zip</c> that mirrors
/// <c>%LocalAppData%\AmbientDirector\</c> so restoring is just "stop the app and unzip over that folder".
/// The zip root holds a consistent SQLite snapshot as <c>ambient-director.db</c> (via <see cref="DbBackupService"/>'s
/// online-backup API, safe against the live connection), plus <c>sounds/</c>, <c>music/</c>, <c>images/</c> and
/// <c>locales/</c> folders carrying the on-disk audio, tile art and translation files that live outside the DB.
///
/// The archive is written to a caller-supplied temp path (the endpoint then streams it with
/// <see cref="FileOptions.DeleteOnClose"/> so the response has a Content-Length). It is best-effort about the
/// data folders: a missing/empty dir is fine (a fresh install yields a zip with just the db), files are opened
/// with permissive sharing (a sound may be playing mid-backup), and a file that vanishes or can't be read is
/// skipped rather than failing the whole download.
/// </summary>
public sealed class FullBackupService(
    DbBackupService db,
    string soundsPath,
    string musicPath,
    string imagesPath,
    string localesPath)
{
    /// <summary>Canonical name of the database snapshot at the zip root.</summary>
    public const string DbEntryName = "ambient-director.db";

    // Configured path → the folder name it lands under in the zip, regardless of where it actually points.
    private IReadOnlyList<(string Path, string Folder)> DataFolders =>
    [
        (soundsPath, "sounds"),
        (musicPath, "music"),
        (imagesPath, "images"),
        (localesPath, "locales"),
    ];

    /// <summary>Write the full backup zip to <paramref name="destinationZipPath"/>.</summary>
    public async Task WriteTo(string destinationZipPath, CancellationToken ct = default)
    {
        // A consistent DB snapshot goes to a sibling temp file first (BackupTo is a plain file writer), then
        // into the zip root; the temp copy is discarded once it's archived.
        var tempDb = destinationZipPath + ".db";
        try
        {
            db.BackupTo(tempDb);

            await using var zipStream = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 4096, FileOptions.Asynchronous);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

            await AddFileAsync(archive, tempDb, DbEntryName, ct);
            foreach (var (path, folder) in DataFolders)
            {
                await AddFolderAsync(archive, path, folder, ct);
            }
        }
        finally
        {
            try { File.Delete(tempDb); } catch { /* best effort — DeleteOnClose owns the zip, this is the db temp */ }
        }
    }

    // Add every file under `dir` as `<folder>/<relative-path>` with forward slashes. Missing/empty dirs are fine.
    private static async Task AddFolderAsync(ZipArchive archive, string dir, string folder, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
            await AddFileAsync(archive, file, $"{folder}/{relative}", ct);
        }
    }

    // Copy one file into the zip under `entryName`. Permissive sharing (audio may be playing); a file that
    // vanished mid-walk or can't be opened is skipped so one bad file never fails the whole backup.
    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken ct)
    {
        FileStream source;
        try
        {
            source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous);
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        await using (source)
        {
            // Fastest: the payload is mostly already-compressed audio/images, so deeper compression buys little.
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await source.CopyToAsync(entryStream, ct);
        }
    }
}
