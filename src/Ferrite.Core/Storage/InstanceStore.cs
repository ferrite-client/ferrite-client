using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Storage;

/// <summary>
/// Reads and writes instance metadata. The filesystem is the source of truth: a metadata file
/// that cannot be parsed is reported and skipped instead of taking the library down.
/// </summary>
public sealed class InstanceStore
{
    private readonly AppPaths _paths;
    private readonly ILogger<InstanceStore> _logger;

    public InstanceStore(AppPaths paths, ILogger<InstanceStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InstanceRecord>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<InstanceRecord>();
        if (!Directory.Exists(_paths.InstancesDirectory))
        {
            return results;
        }

        foreach (var directory in Directory.EnumerateDirectories(_paths.InstancesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await TryLoadFromDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            if (record is not null)
            {
                results.Add(record);
            }
        }

        return results;
    }

    public Task<InstanceRecord?> TryLoadAsync(Guid id, CancellationToken cancellationToken) =>
        TryLoadFromDirectoryAsync(_paths.InstanceDirectory(id), cancellationToken);

    public async Task<InstanceRecord> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await TryLoadAsync(id, cancellationToken).ConfigureAwait(false);
        return record ?? throw new InstanceNotFoundException(id);
    }

    public bool Exists(Guid id) => File.Exists(_paths.InstanceMetadataFile(id));

    public async Task SaveAsync(InstanceRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Id == Guid.Empty)
        {
            throw new ArgumentException("Instance id must be assigned before saving.", nameof(record));
        }

        record.SchemaVersion = InstanceRecord.CurrentSchemaVersion;
        Directory.CreateDirectory(_paths.InstanceDirectory(record.Id));
        Directory.CreateDirectory(_paths.InstanceGameDirectory(record.Id));

        var json = JsonSerializer.Serialize(record, JsonDefaults.Document);
        await AtomicFile
            .WriteAllTextAsync(_paths.InstanceMetadataFile(record.Id), json, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the directory skeleton and persists the record. Nothing is downloaded here; the
    /// install pipeline owns that.
    /// </summary>
    public async Task<InstanceRecord> CreateAsync(InstanceRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        Directory.CreateDirectory(_paths.InstanceDirectory(record.Id));
        var gameDirectory = _paths.InstanceGameDirectory(record.Id);
        Directory.CreateDirectory(gameDirectory);
        foreach (var child in new[] { "mods", "config", "resourcepacks", "shaderpacks", "saves", "logs", "screenshots" })
        {
            Directory.CreateDirectory(Path.Combine(gameDirectory, child));
        }

        await SaveAsync(record, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created instance {Name} ({Id})", record.Name, record.Id);
        return record;
    }

    /// <summary>
    /// Moves the instance directory into the backups folder instead of deleting it in place, so
    /// an accidental delete is recoverable.
    /// </summary>
    public async Task<InstanceRecord?> DeleteAsync(Guid id, bool moveToBackups, CancellationToken cancellationToken)
    {
        var record = await TryLoadAsync(id, cancellationToken).ConfigureAwait(false);
        var directory = _paths.InstanceDirectory(id);
        if (!Directory.Exists(directory))
        {
            return record;
        }

        if (moveToBackups)
        {
            Directory.CreateDirectory(_paths.BackupsDirectory);
            var safeName = PathSafety.SanitizeFileName(record?.Name ?? id.ToString("N"));
            var stem = $"instance-{safeName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{id:N}";
            var destination = Path.Combine(_paths.BackupsDirectory, stem);
            var index = 1;
            while (Directory.Exists(destination))
            {
                destination = Path.Combine(_paths.BackupsDirectory, $"{stem}-{index++}");
            }

            MoveDirectory(directory, destination);
            _logger.LogInformation("Instance {Id} moved to backups at {Destination}", id, destination);
        }
        else
        {
            Directory.Delete(directory, recursive: true);
            _logger.LogInformation("Instance {Id} deleted", id);
        }

        return record;
    }

    public static void MoveDirectory(string source, string destination)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    private async Task<InstanceRecord?> TryLoadFromDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        var metadata = Path.Combine(directory, "instance.json");
        if (!File.Exists(metadata))
        {
            return null;
        }

        try
        {
            var json = await AtomicFile.ReadAllBytesAsync(metadata, cancellationToken).ConfigureAwait(false);
            var record = JsonSerializer.Deserialize<InstanceRecord>(json, JsonDefaults.Document);
            if (record is null)
            {
                _logger.LogWarning("Instance metadata at {Path} was empty", metadata);
                return null;
            }

            if (record.Id == Guid.Empty && Guid.TryParse(Path.GetFileName(directory), out var parsed))
            {
                record.Id = parsed;
            }

            return record;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Instance metadata at {Path} could not be read", metadata);
            return null;
        }
    }
}
