namespace Ferrite.Core.Util;

/// <summary>
/// Guards every write whose path came from outside the process: archive entries, remote
/// metadata, user input, and provider file names.
/// </summary>
public static class PathSafety
{
    private static readonly char[] Separators = ['/', '\\'];

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Validates an archive-style relative path and returns it with normalised separators.
    /// Throws <see cref="PathSafetyException"/> for anything that could escape a destination.
    /// </summary>
    public static string NormalizeRelativePath(string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        if (entryName.IndexOf('\0') >= 0)
        {
            throw new PathSafetyException("Entry name contains a null character.");
        }

        var trimmed = entryName.Trim();
        if (trimmed.Length == 0)
        {
            throw new PathSafetyException("Entry name is empty.");
        }

        if (Path.IsPathRooted(trimmed) || IsDriveQualified(trimmed) || trimmed.StartsWith('~'))
        {
            throw new PathSafetyException($"Entry name is absolute: '{entryName}'.");
        }

        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw new PathSafetyException($"Entry name contains a stream or drive separator: '{entryName}'.");
        }

        var segments = trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new PathSafetyException($"Entry name has no usable segments: '{entryName}'.");
        }

        var normalized = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                throw new PathSafetyException($"Entry name contains a traversal segment: '{entryName}'.");
            }

            if (segment == ".")
            {
                continue;
            }

            var stripped = segment.TrimEnd(' ', '.');
            if (stripped.Length == 0)
            {
                throw new PathSafetyException($"Entry name contains an all-dot segment: '{entryName}'.");
            }

            if (IsReservedDeviceName(stripped))
            {
                throw new PathSafetyException($"Entry name uses a reserved device name: '{entryName}'.");
            }

            normalized.Add(segment);
        }

        if (normalized.Count == 0)
        {
            throw new PathSafetyException($"Entry name has no usable segments: '{entryName}'.");
        }

        return string.Join(Path.DirectorySeparatorChar, normalized);
    }

    /// <summary>
    /// Combines a trusted root with an untrusted relative path and asserts the result stays
    /// inside the root.
    /// </summary>
    public static string ResolveContained(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var normalized = NormalizeRelativePath(relativePath);
        var combined = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsContained(root, combined))
        {
            throw new PathSafetyException($"Path escapes its container: '{relativePath}'.");
        }

        return combined;
    }

    /// <summary>True when <paramref name="candidate"/> resolves inside <paramref name="root"/>.</summary>
    public static bool IsContained(string root, string candidate)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidateFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(rootFull, candidateFull, comparison))
        {
            return true;
        }

        return candidateFull.StartsWith(rootFull + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// Makes a remote-provided file name safe to write on this platform. Invalid characters are
    /// replaced, reserved names are suffixed, and leading dots are preserved only for real
    /// extensions that Minecraft uses.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(invalid.Contains(character) || character == '/' || character == '\\' ? '_' : character);
        }

        var result = builder.ToString().Trim().TrimEnd('.');
        if (result.Length == 0)
        {
            return "unnamed";
        }

        if (IsReservedDeviceName(result))
        {
            result += "_";
        }

        return result.Length > 200 ? result[..200] : result;
    }

    private static bool IsDriveQualified(string value)
    {
        return value.Length >= 2
            && char.IsLetter(value[0])
            && value[1] == ':';
    }

    private static bool IsReservedDeviceName(string segment)
    {
        var stem = segment;
        var dot = stem.IndexOf('.');
        if (dot >= 0)
        {
            stem = stem[..dot];
        }

        return ReservedDeviceNames.Contains(stem.ToUpperInvariant(), StringComparer.Ordinal);
    }

}
