using System.IO.Compression;

namespace Ferrite.Core.Util;

/// <summary>
/// ZIP reading with traversal defence. Every destination is canonicalised and asserted to stay
/// inside the destination root; entry count and expanded size are bounded; links are never
/// materialised.
/// </summary>
public static class ArchiveExtractor
{
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;

    public static async Task<ArchiveExtractionResult> ExtractZipAsync(
        string archivePath,
        string destinationRoot,
        ArchiveExtractionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        destinationRoot = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(destinationRoot);

        using var archive = OpenZip(archivePath);
        var skipped = new List<string>();
        var files = 0;
        long bytes = 0;

        if (archive.Entries.Count > options.MaxEntries)
        {
            throw new PathSafetyException(
                $"Archive declares {archive.Entries.Count} entries which exceeds the {options.MaxEntries} entry limit.");
        }

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSymbolicLink(entry))
            {
                skipped.Add(entry.FullName);
                continue;
            }

            var relative = StripPrefix(entry.FullName, options.StripPrefix);
            if (relative is null)
            {
                continue;
            }

            var isDirectory = string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/');
            string normalized;
            try
            {
                normalized = PathSafety.NormalizeRelativePath(relative);
            }
            catch (PathSafetyException)
            {
                skipped.Add(entry.FullName);
                continue;
            }

            if (options.Include is { } include && !include(normalized))
            {
                continue;
            }

            var target = PathSafety.ResolveContained(destinationRoot, normalized);

            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            if (entry.Length > options.MaxEntryBytes)
            {
                throw new PathSafetyException(
                    $"Entry '{entry.FullName}' declares {entry.Length} bytes which exceeds the per-entry limit.");
            }

            bytes += entry.Length;
            if (bytes > options.MaxTotalBytes)
            {
                throw new PathSafetyException("Archive exceeds the total expanded size limit.");
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!options.Overwrite && File.Exists(target))
            {
                skipped.Add(entry.FullName);
                continue;
            }

            await using (var source = entry.Open())
            await using (var destination = new FileStream(
                target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                await source.CopyToAsync(destination, 128 * 1024, cancellationToken).ConfigureAwait(false);
            }

            files++;
        }

        return new ArchiveExtractionResult(files, bytes, skipped);
    }

    public static async Task<byte[]?> ReadEntryBytesAsync(
        string archivePath,
        string entryPath,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var archive = OpenZip(archivePath);
        var entry = FindEntry(archive, entryPath);
        if (entry is null)
        {
            return null;
        }

        if (entry.Length > maxBytes)
        {
            throw new PathSafetyException($"Entry '{entryPath}' exceeds the {maxBytes} byte read limit.");
        }

        await using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw new PathSafetyException($"Entry '{entryPath}' exceeds the {maxBytes} byte read limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public static string? ReadEntryText(string archivePath, string entryPath, int maxBytes)
    {
        using var archive = OpenZip(archivePath);
        var entry = FindEntry(archive, entryPath);
        if (entry is null)
        {
            return null;
        }

        return ReadBounded(entry, maxBytes, entryPath);
    }

    /// <summary>
    /// Reads an entry with a hard cap on decompressed bytes. The declared length in the archive
    /// header is attacker-controlled, so the stream itself is bounded rather than trusted.
    /// </summary>
    internal static string ReadBounded(ZipArchiveEntry entry, int maxBytes, string entryPath)
    {
        using var source = entry.Open();
        using var reader = new StreamReader(source, System.Text.Encoding.UTF8);
        var buffer = new char[8192];
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            if (builder.Length + read > maxBytes)
            {
                throw new PathSafetyException($"Entry '{entryPath}' exceeds the {maxBytes} byte read limit.");
            }

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> ListEntries(string archivePath)
    {
        using var archive = OpenZip(archivePath);
        var names = new List<string>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            names.Add(entry.FullName);
        }

        return names;
    }

    public static bool ContainsEntry(string archivePath, string entryPath)
    {
        using var archive = OpenZip(archivePath);
        return FindEntry(archive, entryPath) is not null;
    }

    /// <summary>True when an archive entry is a Unix symbolic link rather than a regular file.</summary>
    public static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        if (entry.ExternalAttributes == 0)
        {
            return false;
        }

        var mode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        return mode == UnixSymbolicLink;
    }

    private static ZipArchive OpenZip(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Archive not found.", archivePath);
        }

        return ZipFile.OpenRead(archivePath);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string entryPath)
    {
        var normalizedTarget = entryPath.Replace('\\', '/').TrimStart('/');
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.Equals(name, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static string? StripPrefix(string entryName, string? prefix)
    {
        var normalized = entryName.Replace('\\', '/');
        if (string.IsNullOrEmpty(prefix))
        {
            return normalized;
        }

        var trimmedPrefix = prefix.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == trimmedPrefix.Length
            && normalized.Equals(trimmedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var withSeparator = trimmedPrefix + "/";
        if (!normalized.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized[withSeparator.Length..];
    }
}
