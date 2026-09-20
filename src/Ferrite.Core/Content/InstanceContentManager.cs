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
        foreach (var file in Directory.EnumerateFiles(path))
        {
            var info = new FileInfo(file);
            entries.Add(new ContentFileEntry(
                file,
                info.Name,
                info.Length,
                !info.Name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase),
                info.LastWriteTimeUtc));
        }

        return entries
            .OrderBy(entry => entry.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Enables or disables a content file by renaming it, returning the new path so callers can
    /// update selection state.
    /// </summary>
    public static string SetEnabled(string filePath, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var directory = Path.GetDirectoryName(filePath)
            ?? throw new ArgumentException("The file has no directory.", nameof(filePath));
        var fileName = Path.GetFileName(filePath);
        var isDisabled = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

        if (enabled == !isDisabled)
        {
            return filePath;
        }

        var target = enabled
            ? Path.Combine(directory, fileName[..^DisabledSuffix.Length])
            : Path.Combine(directory, fileName + DisabledSuffix);

        if (File.Exists(target))
        {
            throw new IOException($"Cannot rename to '{Path.GetFileName(target)}' because it already exists.");
        }

        File.Move(filePath, target);
        return target;
    }

    public static void Delete(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
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
