using System.IO.Compression;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Storage;

/// <summary>
/// Instance lifecycle operations that go beyond reading and writing metadata: cloning, renaming, and
/// archiving. Every operation leaves the source instance intact.
/// </summary>
public sealed class InstanceManager
{
    private readonly InstanceStore _store;
    private readonly AppPaths _paths;
    private readonly ILogger<InstanceManager> _logger;

    public InstanceManager(InstanceStore store, AppPaths paths, ILogger<InstanceManager> logger)
    {
        _store = store;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Copies an instance, including its game directory, into a new instance with its own identity.
    /// </summary>
    public async Task<InstanceRecord> CloneAsync(
        Guid sourceId,
        string? newName,
        CancellationToken cancellationToken)
    {
        var source = await _store.LoadAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var name = string.IsNullOrWhiteSpace(newName) ? source.Name + " (copy)" : newName.Trim();

        var clone = new InstanceRecord
        {
            Id = Guid.NewGuid(),
            Name = name,
            MinecraftVersion = source.MinecraftVersion,
            Loader = source.Loader,
            LoaderVersion = source.LoaderVersion,
            JavaPath = source.JavaPath,
            JavaRuntimeComponent = source.JavaRuntimeComponent,
            MemoryMb = source.MemoryMb,
            MinMemoryMb = source.MinMemoryMb,
            JvmArguments = [.. source.JvmArguments],
            GameArguments = [.. source.GameArguments],
            EnvironmentVariables = new Dictionary<string, string>(source.EnvironmentVariables, StringComparer.Ordinal),
            WindowWidth = source.WindowWidth,
            WindowHeight = source.WindowHeight,
            Fullscreen = source.Fullscreen,
            DemoMode = source.DemoMode,
            Group = source.Group,
            AccountId = source.AccountId,
            Notes = source.Notes,
            ModGroups = source.ModGroups
                .Select(group => new ModGroup { Name = group.Name, Match = group.Match })
                .ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
            Modpack = source.Modpack is null
                ? null
                : new ModpackIdentity
                {
                    Provider = source.Modpack.Provider,
                    ProjectId = source.Modpack.ProjectId,
                    VersionId = source.Modpack.VersionId,
                    Name = source.Modpack.Name,
                    VersionName = source.Modpack.VersionName,
                    SourceUrl = source.Modpack.SourceUrl,
                    InstalledAt = source.Modpack.InstalledAt,
                },
        };

        await _store.CreateAsync(clone, cancellationToken).ConfigureAwait(false);

        var sourceGame = _paths.InstanceGameDirectory(sourceId);
        var targetGame = _paths.InstanceGameDirectory(clone.Id);
        if (Directory.Exists(sourceGame))
        {
            await Task.Run(() => InstanceStore.CopyDirectory(sourceGame, targetGame), cancellationToken)
                .ConfigureAwait(false);
        }

        var sourceNatives = Path.Combine(_paths.InstanceDirectory(sourceId), "natives");
        if (Directory.Exists(sourceNatives))
        {
            var targetNatives = Path.Combine(_paths.InstanceDirectory(clone.Id), "natives");
            await Task.Run(() => InstanceStore.CopyDirectory(sourceNatives, targetNatives), cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation("Cloned instance {Source} into {Name} ({Id})", source.Name, name, clone.Id);
        return clone;
    }

    /// <summary>
    /// Renames an instance. The on-disk folder is the instance id, so renaming never risks the files.
    /// </summary>
    public async Task<InstanceRecord> RenameAsync(Guid id, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var record = await _store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
        var previous = record.Name;
        record.Name = newName.Trim();
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Renamed instance {Previous} to {Name}", previous, record.Name);
        return record;
    }

    /// <summary>
    /// Assigns the instance to a folder, or clears the assignment when the name is blank. Only the
    /// metadata changes; the game directory is never touched.
    /// </summary>
    public async Task<InstanceRecord> SetGroupAsync(
        Guid id,
        string? group,
        CancellationToken cancellationToken)
    {
        var record = await _store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
        record.Group = NormalizeGroup(group);
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Instance {Name} assigned to folder {Group}",
            record.Name,
            record.Group ?? "(none)");
        return record;
    }

    /// <summary>The trimmed folder name, or null when it is blank.</summary>
    public static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    /// <summary>
    /// Zips an entire instance, including logs, worlds, and screenshots, without modifying it.
    /// </summary>
    public async Task<string> ArchiveAsync(Guid id, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var record = await _store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
        var directory = _paths.InstanceDirectory(id);
        if (!Directory.Exists(directory))
        {
            throw new InstanceNotFoundException(id);
        }

        var target = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
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
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
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

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            AtomicFile.TryDelete(temporary);
            throw;
        }

        _logger.LogInformation("Archived instance {Name} to {Path}", record.Name, target);
        return target;
    }
}
