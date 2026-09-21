using Ferrite.Core.Content;

namespace Ferrite.Core.Diagnostics;

/// <summary>A mod the crash trace points at, with the evidence for saying so.</summary>
public sealed record ModAttribution(
    string ModId,
    string DisplayName,
    int FrameCount,
    IReadOnlyList<string> Frames)
{
    public string Evidence => FrameCount == 1
        ? "1 stack frame"
        : $"{FrameCount} stack frames";
}

/// <summary>What the analyzer concluded about a crash report.</summary>
public sealed record CrashAnalysis
{
    public required CrashReport Report { get; init; }

    /// <summary>Mods named by the report itself, before any frame matching.</summary>
    public IReadOnlyList<string> SuspectedByReport { get; init; } = [];

    /// <summary>Installed mods whose classes appear in the stack trace, most frames first.</summary>
    public IReadOnlyList<ModAttribution> AttributedMods { get; init; } = [];

    /// <summary>Mods the report lists that are not installed in this instance.</summary>
    public IReadOnlyList<string> MissingMods { get; init; } = [];

    public bool HasFindings => SuspectedByReport.Count > 0 || AttributedMods.Count > 0 || MissingMods.Count > 0;
}

/// <summary>
/// Attributes a crash to installed mods. Two independent signals are used and kept separate: what
/// the report claims, and what the stack frames actually reference. A frame match is evidence about
/// where code ran, not proof of causation, and the summary says so rather than overclaiming.
/// </summary>
public static class CrashAnalyzer
{
    /// <summary>Package roots that belong to the game, the loader, or the JDK rather than a mod.</summary>
    private static readonly string[] NonModRoots =
    [
        "java", "javax", "jdk", "sun", "com/sun", "org/lwjgl", "org/spongepowered",
        "net/minecraft", "net/minecraftforge", "net/neoforged", "cpw/mods",
        "net/fabricmc", "org/quiltmc", "com/mojang", "io/netty", "org/apache", "org/slf4j",
        "com/google", "it/unimi", "org/joml", "oshi",
    ];

    public static CrashAnalysis Analyze(CrashReport report, IReadOnlyList<ModMetadata> installedMods)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(installedMods);

        var tokens = BuildTokens(installedMods);
        var matches = new Dictionary<string, (ModMetadata Mod, List<string> Frames)>(StringComparer.OrdinalIgnoreCase);

        foreach (var frame in report.Frames)
        {
            if (IsGameOrPlatformFrame(frame))
            {
                continue;
            }

            foreach (var token in FrameTokens(frame))
            {
                if (!tokens.TryGetValue(token, out var mod))
                {
                    continue;
                }

                if (!matches.TryGetValue(mod.ModId ?? mod.FileName, out var entry))
                {
                    entry = (mod, []);
                    matches[mod.ModId ?? mod.FileName] = entry;
                }

                entry.Frames.Add(frame.DisplayText);
                break;
            }
        }

        var attributed = matches.Values
            .Select(entry => new ModAttribution(
                entry.Mod.ModId ?? entry.Mod.FileName,
                entry.Mod.DisplayName,
                entry.Frames.Count,
                entry.Frames.Distinct(StringComparer.Ordinal).Take(8).ToList()))
            .OrderByDescending(entry => entry.FrameCount)
            .ThenBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CrashAnalysis
        {
            Report = report,
            SuspectedByReport = report.SuspectedMods,
            AttributedMods = attributed,
            MissingMods = FindMissingMods(report, installedMods),
        };
    }

    /// <summary>
    /// Compares the report's own mod list against what is installed now. A mod the report loaded but
    /// the instance no longer has is a common cause of a crash that will not reproduce.
    /// </summary>
    public static IReadOnlyList<string> FindMissingMods(
        CrashReport report,
        IReadOnlyList<ModMetadata> installedMods)
    {
        if (report.ListedMods.Count == 0)
        {
            return [];
        }

        var installed = BuildTokens(installedMods);
        var missing = new List<string>();
        foreach (var listed in report.ListedMods)
        {
            var id = listed.Split(':', 2)[0].Trim();
            if (id.Length == 0 || IsLoaderOrGameId(id) || installed.ContainsKey(Normalize(id)))
            {
                continue;
            }

            missing.Add(listed);
        }

        return missing;
    }

    private static bool IsGameOrPlatformFrame(CrashFrame frame)
    {
        var joined = string.Join('/', frame.PackageSegments.Take(2)).ToLowerInvariant();
        return NonModRoots.Any(root => joined.StartsWith(root, StringComparison.Ordinal));
    }

    private static IEnumerable<string> FrameTokens(CrashFrame frame)
    {
        foreach (var segment in frame.PackageSegments)
        {
            yield return Normalize(segment);
        }
    }

    private static Dictionary<string, ModMetadata> BuildTokens(IReadOnlyList<ModMetadata> mods)
    {
        var tokens = new Dictionary<string, ModMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            foreach (var candidate in Candidates(mod))
            {
                var token = Normalize(candidate);
                if (token.Length >= 3)
                {
                    tokens.TryAdd(token, mod);
                }
            }
        }

        return tokens;
    }

    private static IEnumerable<string> Candidates(ModMetadata mod)
    {
        if (mod.ModId is { Length: > 0 } id)
        {
            yield return id;
            // Mod ids and packages differ in punctuation: "cloth-config" ships as "cloth_config".
            foreach (var part in id.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries))
            {
                yield return part;
            }
        }

        var stem = Path.GetFileNameWithoutExtension(mod.FileName);
        if (stem.Length > 0)
        {
            yield return stem;
            foreach (var part in stem.Split(['-', '_', '+', '.'], StringSplitOptions.RemoveEmptyEntries))
            {
                yield return part;
            }
        }
    }

    private static bool IsLoaderOrGameId(string id) => Normalize(id) switch
    {
        "minecraft" or "java" or "fabricloader" or "fabric" or "forge" or "neoforge" or "quiltloader" => true,
        _ => false,
    };

    private static string Normalize(string value) =>
        value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
