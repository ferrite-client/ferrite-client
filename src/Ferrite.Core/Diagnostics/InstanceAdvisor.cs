using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Diagnostics;

/// <summary>How much a finding matters, from a blocking problem down to context.</summary>
public enum AdvisorSeverity
{
    Info,
    Low,
    Medium,
    High,
}

/// <summary>One conclusion, the evidence for it, and the action it implies.</summary>
public sealed record AdvisorFinding(
    string Code,
    AdvisorSeverity Severity,
    string Title,
    string Detail,
    string Action);

/// <summary>Everything the advisor is allowed to look at, gathered by the caller.</summary>
public sealed record AdvisorInputs
{
    public required InstanceRecord Instance { get; init; }

    public IReadOnlyList<ModMetadata> Mods { get; init; } = [];

    /// <summary>Crash reports found in the instance, newest first.</summary>
    public IReadOnlyList<CrashReport> CrashReports { get; init; } = [];

    /// <summary>Issues from the last launch preflight, if a launch has been attempted.</summary>
    public IReadOnlyList<PreflightIssue> Preflight { get; init; } = [];

    /// <summary>The newest log's tail, or null when there is no log.</summary>
    public string? LogText { get; init; }

    /// <summary>Whether any Java runtime was discovered at all.</summary>
    public bool JavaAvailable { get; init; } = true;
}

/// <summary>What the advisor concluded, worst first.</summary>
public sealed record AdvisorReport
{
    public required IReadOnlyList<AdvisorFinding> Findings { get; init; }

    public bool HasProblems => Findings.Any(finding => finding.Severity >= AdvisorSeverity.Medium);
}

/// <summary>
/// Explains a broken instance from the evidence the instance already holds. This is deliberately not
/// a model: every finding is a rule over the crash report, the mod metadata, the log tail, or the
/// preflight result, so the same inputs always produce the same answer and each conclusion can be
/// checked against the line that produced it. Where a conclusion is a starting point rather than a
/// verdict, the text says so.
/// </summary>
public static class InstanceAdvisor
{
    /// <summary>Log signatures that name a known cause, with the action each one implies.</summary>
    private static readonly (string Code, string Needle, AdvisorSeverity Severity, string Title, string Action)[]
        LogSignatures =
        [
            ("log-duplicate-mods", "Duplicate mod", AdvisorSeverity.High,
                "Two copies of the same mod are installed",
                "Remove one copy of each duplicated mod."),
            ("log-incompatible-set", "Incompatible mod set", AdvisorSeverity.High,
                "The loader refused the mod set",
                "Read the named mods in the log and remove or replace the offending one."),
            ("log-missing-dependency", "Missing or unsupported mandatory dependencies", AdvisorSeverity.Medium,
                "A mod's required dependency is missing",
                "Install the dependency the log names for the affected mod."),
            ("log-out-of-memory", "OutOfMemoryError", AdvisorSeverity.High,
                "The game ran out of memory",
                "Raise this instance's maximum memory, or reduce the number of mods."),
            ("log-mixin-apply", "Mixin apply failed", AdvisorSeverity.Medium,
                "A mod failed to apply a mixin",
                "Check the mixin's target mod against the loader and Minecraft version."),
            ("log-no-class", "NoClassDefFoundError", AdvisorSeverity.Medium,
                "A class a mod expected was not present",
                "A dependency is missing or the wrong version; the log names the class."),
            ("log-no-method", "NoSuchMethodError", AdvisorSeverity.Medium,
                "A mod called a method that is not present",
                "A dependency is the wrong version for this Minecraft version."),
        ];

    /// <summary>Dependency ids the loader or the game provides, so they are never "missing".</summary>
    private static readonly HashSet<string> ProvidedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "fabricloader", "fabric", "fabric-api", "forge", "neoforge",
        "quiltloader", "quilt_loader", "quilted_fabric_api", "fabric_api",
    };

    public static AdvisorReport Advise(AdvisorInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var findings = new List<AdvisorFinding>();

        if (!inputs.JavaAvailable)
        {
            findings.Add(new AdvisorFinding(
                "java-none",
                AdvisorSeverity.High,
                "No Java runtime was found",
                "Nothing on PATH, in the launcher's own runtimes, or in the managed store looked like a "
                + "Java executable.",
                "Open the Java page and add a runtime, or let Ferrite download a suitable one."));
        }

        AppendPreflight(findings, inputs.Preflight);
        AppendCrashes(findings, inputs);
        AppendMissingDependencies(findings, inputs.Mods);
        AppendLogFindings(findings, inputs.LogText);
        AppendContext(findings, inputs);

        if (findings.Count == 0)
        {
            findings.Add(new AdvisorFinding(
                "clean",
                AdvisorSeverity.Info,
                "No problems found",
                "Nothing in the crash reports, the log, the installed mods, or the last preflight "
                + "looked like a cause.",
                "Launch the instance and, if it fails, press this again with the new log in place."));
        }

        var ordered = findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ToList();
        return new AdvisorReport { Findings = ordered };
    }

    private static void AppendPreflight(List<AdvisorFinding> findings, IReadOnlyList<PreflightIssue> preflight)
    {
        foreach (var issue in preflight.Where(issue => issue.IsBlocking))
        {
            findings.Add(new AdvisorFinding(
                $"preflight-{issue.Code}",
                AdvisorSeverity.High,
                "The last launch was blocked before it started",
                issue.Message,
                ActionFor(issue.Code)));
        }
    }

    private static string ActionFor(string code) => code switch
    {
        "client-missing" or "libraries-missing" or "natives-missing" or "verify-missing-files" =>
            "Press Repair on this instance.",
        "java-missing" or "java-incompatible" =>
            "Choose a different Java runtime in the instance's settings.",
        "memory-too-low" => "Raise the instance's maximum memory.",
        _ => "Resolve the reported problem and launch again.",
    };

    private static void AppendCrashes(List<AdvisorFinding> findings, AdvisorInputs inputs)
    {
        if (inputs.CrashReports.Count == 0)
        {
            return;
        }

        var newest = inputs.CrashReports[0];
        var analysis = CrashAnalyzer.Analyze(newest, inputs.Mods);

        if (analysis.MissingMods.Count > 0)
        {
            findings.Add(new AdvisorFinding(
                "crash-missing-mods",
                AdvisorSeverity.High,
                "The crash report loaded mods that are not installed now",
                $"The report lists {string.Join(", ", analysis.MissingMods.Take(6))}"
                + (analysis.MissingMods.Count > 6 ? $" and {analysis.MissingMods.Count - 6} more" : string.Empty)
                + ". A crash that names an absent mod usually will not reproduce until it is back.",
                "Reinstall those mods, or remove the mods that require them."));
        }

        if (analysis.AttributedMods.Count > 0)
        {
            var named = analysis.AttributedMods
                .Take(3)
                .Select(mod => $"{mod.DisplayName} ({mod.Evidence})");
            findings.Add(new AdvisorFinding(
                "crash-frames",
                AdvisorSeverity.Medium,
                "Installed mods appear in the last crash's stack trace",
                $"Code from {string.Join(", ", named)} ran in the failing frame(s). A frame is evidence "
                + "that code ran, not proof of cause.",
                "Disable those mods one at a time and launch after each change."));
        }
        else if (analysis.SuspectedByReport.Count > 0)
        {
            findings.Add(new AdvisorFinding(
                "crash-suspects",
                AdvisorSeverity.Medium,
                "The crash report names mods as suspects",
                string.Join(", ", analysis.SuspectedByReport.Take(6)),
                "Disable the named mods one at a time and launch after each change."));
        }
    }

    /// <summary>
    /// A declared dependency that is not installed and is not provided by the loader. Optional
    /// dependencies are left out: the mod loader reads them as advice, not a requirement.
    /// </summary>
    private static void AppendMissingDependencies(List<AdvisorFinding> findings, IReadOnlyList<ModMetadata> mods)
    {
        if (mods.Count == 0)
        {
            return;
        }

        var installed = mods
            .Select(mod => mod.ModId ?? Path.GetFileNameWithoutExtension(mod.FileName))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            foreach (var dependency in mod.Dependencies)
            {
                var id = dependency.Split(':', 2)[0].Trim();
                if (id.Length == 0 || ProvidedIds.Contains(id) || installed.Contains(Normalize(id)))
                {
                    continue;
                }

                if (!missing.TryGetValue(id, out var dependents))
                {
                    dependents = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    missing[id] = dependents;
                }

                dependents.Add(mod.DisplayName);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        var described = missing
            .Take(6)
            .Select(pair => $"{pair.Key} (needed by {string.Join(", ", pair.Value)})");
        findings.Add(new AdvisorFinding(
            "mod-missing-dependency",
            AdvisorSeverity.Medium,
            $"{missing.Count} required mod dependency(ies) are not installed",
            string.Join("; ", described),
            "Install the missing projects from Browse, then launch again."));
    }

    private static void AppendLogFindings(List<AdvisorFinding> findings, string? logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return;
        }

        foreach (var signature in LogSignatures)
        {
            var line = LastLineContaining(logText, signature.Needle);
            if (line is null)
            {
                continue;
            }

            findings.Add(new AdvisorFinding(
                signature.Code,
                signature.Severity,
                signature.Title,
                $"The log says: {line}",
                signature.Action));
        }
    }

    private static void AppendContext(List<AdvisorFinding> findings, AdvisorInputs inputs)
    {
        var disabled = inputs.Mods.Count(mod => !mod.Enabled);
        if (disabled > 0)
        {
            findings.Add(new AdvisorFinding(
                "mods-disabled",
                AdvisorSeverity.Info,
                $"{disabled} mod(s) are disabled",
                "Disabled mods are renamed rather than removed, so they are still in the instance.",
                "Enable them again from the Mods tab if the instance used to work with them."));
        }
    }

    /// <summary>The last line containing the needle, trimmed to a readable length.</summary>
    private static string? LastLineContaining(string logText, string needle)
    {
        string? found = null;
        foreach (var line in logText.Split('\n'))
        {
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = line.Trim().TrimEnd('\r');
                found = trimmed.Length > 240 ? trimmed[..240] + "..." : trimmed;
            }
        }

        return found;
    }

    private static string Normalize(string value) =>
        value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
