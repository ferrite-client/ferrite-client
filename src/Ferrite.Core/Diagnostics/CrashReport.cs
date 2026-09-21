using System.Globalization;
using System.Text.RegularExpressions;

namespace Ferrite.Core.Diagnostics;

/// <summary>One frame of a crash stack trace.</summary>
public sealed record CrashFrame(string ClassName, string MethodName, string? FileName, int? LineNumber)
{
    /// <summary>The package part of the class name, used to attribute a frame to a mod.</summary>
    public IReadOnlyList<string> PackageSegments =>
        ClassName.Split('.', StringSplitOptions.RemoveEmptyEntries);

    public string DisplayText => FileName is null
        ? $"{ClassName}.{MethodName}"
        : LineNumber is { } line
            ? $"{ClassName}.{MethodName}({FileName}:{line})"
            : $"{ClassName}.{MethodName}({FileName})";
}

/// <summary>
/// A parsed Minecraft crash report. The format is produced by the game, not by a specification, so
/// the parser reads the sections that exist across vanilla, Fabric, Forge, and NeoForge reports and
/// ignores anything it does not recognise.
/// </summary>
public sealed record CrashReport
{
    public required string Path { get; init; }

    public string? Description { get; init; }

    public DateTimeOffset? Time { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public IReadOnlyList<CrashFrame> Frames { get; init; } = [];

    /// <summary>Mod ids the report itself names as suspects.</summary>
    public IReadOnlyList<string> SuspectedMods { get; init; } = [];

    /// <summary>Every mod the report lists as loaded, as "id: name version".</summary>
    public IReadOnlyList<string> ListedMods { get; init; } = [];

    /// <summary>Values from the "System Details" section, such as Minecraft version.</summary>
    public IReadOnlyDictionary<string, string> SystemDetails { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? FileName => System.IO.Path.GetFileName(Path);

    public string Summary => ExceptionType is null
        ? Description ?? "Crash report"
        : string.IsNullOrEmpty(ExceptionMessage)
            ? ExceptionType
            : $"{ExceptionType}: {ExceptionMessage}";
}

public static class CrashReportParser
{
    private const string HeaderMarker = "---- Minecraft Crash Report ----";

    private static readonly Regex SectionPattern = new(@"^--\s*(?<name>.+?)\s*--$", RegexOptions.Compiled);
    private static readonly Regex FramePattern = new(
        @"^\s+at\s+(?<class>[\w.$]+)\.(?<method>[\w$<>]+)\((?<file>[^)]*)\)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex ExceptionPattern = new(
        @"^(?<type>(?:[\w$]+\.)+[A-Za-z_$][\w$]*(?:Exception|Error|Throwable))(?::\s*(?<message>.*))?$",
        RegexOptions.Compiled);
    private static readonly Regex KeyValuePattern = new(
        @"^\s+(?<key>[A-Za-z][A-Za-z0-9 _./()-]{0,60}):\s*(?<value>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex ModEntryPattern = new(
        @"^\s+(?<id>[a-z0-9_][a-z0-9_-]{1,63}):\s+(?<rest>\S.*)$",
        RegexOptions.Compiled);

    /// <summary>True when the text starts like a client crash report.</summary>
    public static bool LooksLikeCrashReport(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains(HeaderMarker, StringComparison.Ordinal);

    public static CrashReport Parse(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);

        string? description = null;
        DateTimeOffset? time = null;
        string? exceptionType = null;
        string? exceptionMessage = null;
        var frames = new List<CrashFrame>();
        var suspected = new List<string>();
        var listedMods = new List<string>();
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var section = string.Empty;
        var inSuspectedMods = false;
        var inModList = false;

        foreach (var rawLine in EnumerateLines(text))
        {
            if (SectionPattern.Match(rawLine) is { Success: true } heading)
            {
                section = heading.Groups["name"].Value.Trim();
                inModList = section.Equals("Mod List", StringComparison.OrdinalIgnoreCase);
                inSuspectedMods = false;
                continue;
            }

            if (rawLine.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
            {
                description ??= rawLine["Description:".Length..].Trim();
                inSuspectedMods = false;
                inModList = false;
                continue;
            }

            if (rawLine.StartsWith("Time:", StringComparison.OrdinalIgnoreCase) && time is null)
            {
                var text2 = rawLine["Time:".Length..].Trim();
                if (DateTimeOffset.TryParse(text2, CultureInfo.InvariantCulture, out var parsed))
                {
                    time = parsed;
                }
                else if (DateTime.TryParse(text2, CultureInfo.InvariantCulture, out var local))
                {
                    time = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local));
                }
            }

            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith("Suspected Mods:", StringComparison.OrdinalIgnoreCase))
            {
                AddModTokens(suspected, trimmed["Suspected Mods:".Length..]);
                inSuspectedMods = true;
                inModList = false;
                continue;
            }

            if (string.Equals(trimmed, "Fabric Mods:", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "Mods:", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "Jar Mods:", StringComparison.OrdinalIgnoreCase))
            {
                inModList = true;
                inSuspectedMods = false;
                continue;
            }

            if (FramePattern.Match(rawLine) is { Success: true } frame)
            {
                var (file, line) = ParseFrameFile(frame.Groups["file"].Value);
                frames.Add(new CrashFrame(
                    frame.Groups["class"].Value,
                    frame.Groups["method"].Value,
                    file,
                    line));
                inSuspectedMods = false;
                continue;
            }

            if (exceptionType is null
                && !rawLine.StartsWith('\t')
                && ExceptionPattern.Match(trimmed) is { Success: true } exception)
            {
                exceptionType = exception.Groups["type"].Value;
                var message = exception.Groups["message"].Success ? exception.Groups["message"].Value.Trim() : null;
                exceptionMessage = string.IsNullOrEmpty(message) ? null : message;
                continue;
            }

            if (inSuspectedMods)
            {
                if (rawLine.StartsWith('\t') && !string.IsNullOrWhiteSpace(trimmed))
                {
                    AddModTokens(suspected, trimmed);
                    continue;
                }

                inSuspectedMods = false;
            }

            if (inModList && ModEntryPattern.Match(rawLine) is { Success: true } mod)
            {
                listedMods.Add($"{mod.Groups["id"].Value}: {mod.Groups["rest"].Value.Trim()}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(section)
                && KeyValuePattern.Match(rawLine) is { Success: true } pair)
            {
                var key = pair.Groups["key"].Value.Trim();
                var value = pair.Groups["value"].Value.Trim();
                if (key.Length > 0 && value.Length > 0)
                {
                    details.TryAdd(key, value);
                }
            }
        }

        return new CrashReport
        {
            Path = path,
            Description = description,
            Time = time,
            ExceptionType = exceptionType,
            ExceptionMessage = exceptionMessage,
            // The same trace appears both inline and under "-- Head --"; identical frames carry no
            // extra information, so they are collapsed.
            Frames = frames.DistinctBy(frame => frame.DisplayText).ToList(),
            SuspectedMods = suspected,
            ListedMods = listedMods,
            SystemDetails = details,
        };
    }

    private static (string? File, int? Line) ParseFrameFile(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
        {
            return (text[..colon], line);
        }

        return (text, null);
    }

    private static void AddModTokens(List<string> target, string list)
    {
        foreach (var token in list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!target.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(token);
            }
        }
    }

    private static IEnumerable<string> EnumerateLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
