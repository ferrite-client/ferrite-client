using Ferrite.Core.Util;

namespace Ferrite.Core.Content;

/// <summary>
/// Instance content on disk: mods and packs, enable/disable without data loss, deletion, and size
/// accounting. Disabling renames the file and never rewrites it.
/// </summary>
public sealed class InstanceContentManager
{
    public const string DisabledSuffix = ".disabled";

    private readonly ModScanner _scanner;

    public InstanceContentManager(ModScanner scanner)
    {
        _scanner = scanner;
    }

    public Task<IReadOnlyList<ModMetadata>> ListModsAsync(string gameDirectory, CancellationToken cancellationToken) =>
        _scanner.ScanAsync(Path.Combine(gameDirectory, "mods"), cancellationToken);

    public static IReadOnlyList<ContentFileEntry> ListFolder(string gameDirectory, string folder)
    {
        var path = Path.Combine(gameDirectory, folder);
        if (!Directory.Exists(path))
        {
            return [];
        }

        var entries = new List<ContentFileEntry>();
        foreach (var item in Directory.EnumerateFileSystemEntries(path))
        {
            // A resource pack or datapack may ship as a folder rather than a zip, so both are listed.
            var isDirectory = Directory.Exists(item);
            var info = new FileInfo(item);
            entries.Add(new ContentFileEntry(
                item,
                info.Name,
                isDirectory ? GetDirectorySize(item) : info.Length,
                !info.Name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase),
                info.LastWriteTimeUtc));
        }

        return entries
            .OrderBy(entry => entry.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Lists a pack folder with what each pack declares. A pack that ships a <c>pack.mcmeta</c> whose
    /// formats exclude this instance is marked, because the game will silently ignore it.
    /// </summary>
    public static IReadOnlyList<ContentFileEntry> ListPacks(
        string gameDirectory,
        string folder,
        int? instanceFormat)
    {
        var entries = new List<ContentFileEntry>();
        foreach (var entry in ListFolder(gameDirectory, folder))
        {
            // A disabled pack is renamed, not modified, so it is still a readable ZIP.
            var name = entry.FileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
                ? entry.FileName[..^DisabledSuffix.Length]
                : entry.FileName;
            var metadata = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? PackMetadataReader.ReadFromZip(entry.FilePath)
                : entry.Enabled
                    ? PackMetadataReader.ReadFromDirectory(entry.FilePath)
                    : null;

            var compatibility = PackMetadataReader.Evaluate(metadata, instanceFormat);
            entries.Add(entry with
            {
                PackFormatText = metadata?.FormatText,
                CompatibilityText = metadata is null
                    ? null
                    : PackMetadataReader.DescribeCompatibility(compatibility, instanceFormat),
                IsPackMismatch = compatibility == PackCompatibility.Mismatch,
            });
        }

        return entries;
    }

    /// <summary>
    /// Enables or disables a content file by renaming it, returning the new path so callers can
    /// update selection state.
    /// </summary>
    /// <summary>
    /// Enables or disables a piece of content by renaming it, so nothing is rewritten. Works for a
    /// file (a mod jar, a pack zip) and for a directory (a resource pack that ships as a folder).
    /// </summary>
    public static string SetEnabled(string path, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The content has no directory.", nameof(path));
        var fileName = Path.GetFileName(path);
        var isDisabled = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

        if (enabled == !isDisabled)
        {
            return path;
        }

        var target = enabled
            ? Path.Combine(directory, fileName[..^DisabledSuffix.Length])
            : Path.Combine(directory, fileName + DisabledSuffix);

        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException($"Cannot rename to '{Path.GetFileName(target)}' because it already exists.");
        }

        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }

        return target;
    }

    public static void Delete(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Moves a content file out of an instance into a backup folder instead of deleting it. A mod a
    /// user dropped in is their file, so removing it from the instance keeps a copy rather than
    /// destroying it. Returns where the file went, or null when there was nothing to move.
    /// </summary>
    public static string? RemoveToBackup(string path, string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            return null;
        }

        Directory.CreateDirectory(backupDirectory);
        var fileName = Path.GetFileName(path);
        var target = Path.Combine(
            backupDirectory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{PathSafety.SanitizeFileName(fileName)}");

        // Two removals inside the same second must not overwrite one another.
        var unique = target;
        for (var index = 1; File.Exists(unique) || Directory.Exists(unique); index++)
        {
            unique = Path.Combine(
                backupDirectory,
                $"{Path.GetFileNameWithoutExtension(target)}-{index}{Path.GetExtension(target)}");
        }

        if (isDirectory)
        {
            Directory.Move(path, unique);
        }
        else
        {
            File.Move(path, unique);
        }

        return unique;
    }

    public static long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
        }

        return total;
    }
}
