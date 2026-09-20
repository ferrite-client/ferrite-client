using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Platform;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Storage;

/// <summary>
/// Loads and saves launcher settings. A document from a newer schema is preserved as a backup
/// rather than silently mangled, and a corrupt document never blocks startup.
/// </summary>
public sealed class SettingsStore
{
    private readonly AppPaths _paths;
    private readonly ILogger<SettingsStore> _logger;

    public SettingsStore(AppPaths paths, ILogger<SettingsStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public LauncherSettings Current { get; private set; } = new();

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var file = _paths.SettingsFile;
        if (!File.Exists(file))
        {
            Current = new LauncherSettings();
            return Current;
        }

        try
        {
            var json = await AtomicFile.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<LauncherSettings>(json, JsonDefaults.Document);
            if (loaded is null)
            {
                throw new JsonException("Settings document was empty.");
            }

            Current = Migrate(loaded, out var migrated);
            if (migrated)
            {
                _logger.LogInformation("Settings migrated to schema version {Version}", Current.SchemaVersion);
                await SaveAsync(cancellationToken).ConfigureAwait(false);
            }

            return Current;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Settings could not be read; preserving the file and starting from defaults");
            TryBackup(file);
            Current = new LauncherSettings();
            return Current;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Current.SchemaVersion = LauncherSettings.CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(Current, JsonDefaults.Document);
        await AtomicFile.WriteAllTextAsync(_paths.SettingsFile, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies forward migrations. Returns the settings to use and whether a save is required.
    /// </summary>
    public static LauncherSettings Migrate(LauncherSettings settings, out bool migrated)
    {
        migrated = false;

        if (settings.SchemaVersion > LauncherSettings.CurrentSchemaVersion)
        {
            throw new JsonException(
                $"Settings schema version {settings.SchemaVersion} is newer than this build supports "
                + $"({LauncherSettings.CurrentSchemaVersion}).");
        }

        if (settings.SchemaVersion < 1)
        {
            settings.SchemaVersion = 1;
            migrated = true;
        }

        if (settings.MaxConcurrentDownloads is < 1 or > 64)
        {
            settings.MaxConcurrentDownloads = 8;
            migrated = true;
        }

        return settings;
    }

    private void TryBackup(string file)
    {
        try
        {
            Directory.CreateDirectory(_paths.BackupsDirectory);
            var name = $"settings-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
            AtomicFile.CopyReplacing(file, Path.Combine(_paths.BackupsDirectory, name));
        }
        catch (IOException)
        {
        }
    }
}
