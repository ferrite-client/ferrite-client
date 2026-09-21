using Ferrite.Core.Util;

namespace Ferrite.Core.Platform;

/// <summary>Why a chosen data folder cannot be used, or what moving to it would cost.</summary>
public sealed record DataRootValidation(bool IsValid, IReadOnlyList<string> Issues, long TotalBytes)
{
    public string? Summary => Issues.Count == 0 ? null : string.Join(" ", Issues);
}

/// <summary>What a completed move copied.</summary>
public sealed record DataRootMoveResult(string Target, int FilesCopied, long BytesCopied);

/// <summary>
/// Moves the launcher's data folder. The launcher cannot swap its own root while it is running - every
/// open handle and half-finished download points at the old one - so the move copies the data, records
/// the new root in a marker file next to the executable, and the user restarts.
/// </summary>
public static class DataRootRelocator
{
    /// <summary>Marker written into the destination so the launcher recognises it as its own.</summary>
    public const string OwnershipMarkerFileName = ".ferrite-data-root";

    /// <summary>
    /// Decides whether a target folder can be used, before anything is copied. A path that is the same
    /// as the current root, nested inside it, or an ancestor of it would produce either a no-op or a
    /// partially copied tree, so all three are refused with a reason.
    /// </summary>
    public static DataRootValidation Validate(string currentRoot, string target)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(target))
        {
            return new DataRootValidation(false, ["Choose a folder for the launcher's data."], 0);
        }

        if (!Path.IsPathFullyQualified(target))
        {
            return new DataRootValidation(false, ["The folder path has to be absolute."], 0);
        }

        string currentFull;
        string targetFull;
        try
        {
            currentFull = Path.GetFullPath(currentRoot);
            targetFull = Path.GetFullPath(target);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new DataRootValidation(false, [$"That path cannot be used: {exception.Message}"], 0);
        }

        if (PathsEqual(currentFull, targetFull))
        {
            issues.Add("That is already the launcher's data folder.");
        }

        if (IsInside(targetFull, currentFull))
        {
            issues.Add("The new folder cannot be inside the current one.");
        }

        if (IsInside(currentFull, targetFull))
        {
            issues.Add("The new folder cannot contain the current one.");
        }

        var totalBytes = DirectorySize(currentFull);

        if (issues.Count == 0 && Directory.Exists(targetFull))
        {
            var entries = Directory.EnumerateFileSystemEntries(targetFull).Take(2).ToList();
            var owned = File.Exists(Path.Combine(targetFull, OwnershipMarkerFileName));
            if (owned)
            {
                // Moving onto an existing launcher folder would merge two data sets; refuse it.
                issues.Add("That folder already holds a Ferrite data set. Choose an empty folder.");
            }
            else if (entries.Count > 0)
            {
                issues.Add("That folder is not empty and was not created by Ferrite.");
            }
        }

        try
        {
            if (Directory.Exists(targetFull))
            {
                var probe = Path.Combine(targetFull, $".ferrite-write-probe-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
            }
            else
            {
                var parent = Path.GetDirectoryName(targetFull);
                if (parent is null || !Directory.Exists(parent))
                {
                    issues.Add("The parent folder does not exist.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add($"The folder cannot be written to: {exception.Message}");
        }

        return new DataRootValidation(issues.Count == 0, issues, totalBytes);
    }

    /// <summary>
    /// Copies the data root to a validated target. Files are copied through a temporary sibling and
    /// moved into place, so an interrupted move never leaves a half-written file looking complete.
    /// </summary>
    public static async Task<DataRootMoveResult> MoveAsync(
        string currentRoot,
        string target,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var validation = Validate(currentRoot, target);
        if (!validation.IsValid)
        {
            throw new IOException(validation.Summary ?? "The target folder cannot be used.");
        }

        var source = Path.GetFullPath(currentRoot);
        var destination = Path.GetFullPath(target);
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(destination, OwnershipMarkerFileName),
            "Ferrite data root",
            cancellationToken).ConfigureAwait(false);

        var files = 0;
        long bytes = 0;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var targetFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

            await using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var temporary = Path.Combine(
                    Path.GetDirectoryName(targetFile)!,
                    $".{Path.GetFileName(targetFile)}.part-{Guid.NewGuid():N}");
                try
                {
                    await using (var output = new FileStream(
                                     temporary,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None))
                    {
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }

                    File.Move(temporary, targetFile, overwrite: true);
                }
                catch
                {
                    AtomicFile.TryDelete(temporary);
                    throw;
                }
            }

            files++;
            bytes += new FileInfo(targetFile).Length;
            if (files % 50 == 0)
            {
                progress?.Report($"{files} file(s) copied...");
            }
        }

        progress?.Report($"Copied {files} file(s).");
        return new DataRootMoveResult(destination, files, bytes);
    }

    /// <summary>
    /// Records the chosen root so the next start uses it. Written next to the executable, which is
    /// where <see cref="AppPaths.CreateDefault"/> looks.
    /// </summary>
    public static void WriteRootMarker(string target, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var marker = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, AppPaths.RootMarkerFileName);
        AtomicFile.WriteAllText(marker, Path.GetFullPath(target));
    }

    /// <summary>Removes the marker so the launcher returns to its default or portable location.</summary>
    public static bool ClearRootMarker(string? baseDirectory = null)
    {
        var marker = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, AppPaths.RootMarkerFileName);
        if (!File.Exists(marker))
        {
            return false;
        }

        File.Delete(marker);
        return true;
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first.TrimEnd(Path.DirectorySeparatorChar),
            second.TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsInside(string candidate, string parent)
    {
        var normalisedParent = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(
            normalisedParent,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static long DirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A locked or unreadable file simply does not count towards the estimate.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return total;
    }
}
