using System.IO.Compression;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Game;

/// <summary>
/// World backup, restore, duplicate, and delete. Deleting a world moves it into the backups folder
/// instead of unlinking it, because a world cannot be re-downloaded.
/// </summary>
public sealed class WorldArchive
{
    private readonly string _backupsDirectory;
    private readonly ILogger<WorldArchive> _logger;

    public WorldArchive(string backupsDirectory, ILogger<WorldArchive> logger)
    {
        _backupsDirectory = backupsDirectory;
        _logger = logger;
    }

    /// <summary>Zips a world into the backups folder and returns the archive path.</summary>
    public async Task<string> BackupAsync(string worldDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(worldDirectory))
        {
            throw new DirectoryNotFoundException(worldDirectory);
        }

        Directory.CreateDirectory(_backupsDirectory);
        var name = PathSafety.SanitizeFileName(Path.GetFileName(worldDirectory));
        var stem = $"world-{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var destination = Path.Combine(_backupsDirectory, stem + ".zip");
        var suffix = 1;
        while (File.Exists(destination))
        {
            destination = Path.Combine(_backupsDirectory, $"{stem}-{suffix++}.zip");
        }

        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var file in Directory.EnumerateFiles(worldDirectory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(worldDirectory, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var source = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: 128 * 1024,
                        useAsync: true);
                    await source.CopyToAsync(entryStream, 128 * 1024, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, destination, overwrite: true);
            _logger.LogInformation("Backed up world {World} to {Path}", worldDirectory, destination);
            return destination;
        }
        catch
        {
            AtomicFile.TryDelete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Restores a world archive into the saves folder. An existing folder with the same name is moved
    /// into backups first, so a restore never silently overwrites a newer world.
    /// </summary>
    public async Task<string> RestoreAsync(
        string archivePath,
        string gameDirectory,
        string? folderName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("World archive not found.", archivePath);
        }

        var saves = WorldService.SavesDirectory(gameDirectory);
        Directory.CreateDirectory(saves);
        var name = PathSafety.SanitizeFileName(folderName ?? Path.GetFileNameWithoutExtension(archivePath));
        var destination = Path.Combine(saves, name);
        if (!PathSafety.IsContained(saves, destination))
        {
            throw new PathSafetyException("The world folder name escapes the saves directory.");
        }

        if (Directory.Exists(destination))
        {
            Directory.CreateDirectory(_backupsDirectory);
            var displaced = Path.Combine(
                _backupsDirectory,
                $"replaced-{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
            InstanceStore.MoveDirectory(destination, displaced);
            _logger.LogInformation("Moved the previous {World} to {Path}", name, displaced);
        }

        var result = await ArchiveExtractor
            .ExtractZipAsync(archivePath, destination, new ArchiveExtractionOptions(), cancellationToken)
            .ConfigureAwait(false);

        if (result.FilesExtracted == 0)
        {
            throw new NbtException("The archive contained no world files.");
        }

        _logger.LogInformation("Restored {Count} file(s) into {Path}", result.FilesExtracted, destination);
        return destination;
    }

    /// <summary>Copies a world to a new folder name inside the saves directory.</summary>
    public string Duplicate(string worldDirectory, string gameDirectory, string? newFolderName)
    {
        var saves = WorldService.SavesDirectory(gameDirectory);
        var source = Path.GetFileName(worldDirectory);
        var name = PathSafety.SanitizeFileName(newFolderName ?? source + "-copy");
        var destination = Path.Combine(saves, name);
        if (!PathSafety.IsContained(saves, destination))
        {
            throw new PathSafetyException("The world folder name escapes the saves directory.");
        }

        var suffix = 1;
        while (Directory.Exists(destination))
        {
            destination = Path.Combine(saves, $"{name}-{suffix++}");
        }

        InstanceStore.CopyDirectory(worldDirectory, destination);
        _logger.LogInformation("Duplicated world {Source} to {Destination}", source, destination);
        return destination;
    }

    /// <summary>Moves a world into backups rather than deleting it in place.</summary>
    public string Delete(string worldDirectory)
    {
        var name = PathSafety.SanitizeFileName(Path.GetFileName(worldDirectory));
        Directory.CreateDirectory(_backupsDirectory);
        var stem = $"deleted-{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var destination = Path.Combine(_backupsDirectory, stem);
        var suffix = 1;
        while (Directory.Exists(destination))
        {
            destination = Path.Combine(_backupsDirectory, $"{stem}-{suffix++}");
        }

        InstanceStore.MoveDirectory(worldDirectory, destination);
        _logger.LogInformation("Moved world {World} to {Path}", name, destination);
        return destination;
    }

    public IReadOnlyList<string> ListBackups() =>
        Directory.Exists(_backupsDirectory)
            ? Directory.EnumerateFiles(_backupsDirectory, "world-*.zip")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList()
            : [];
}
