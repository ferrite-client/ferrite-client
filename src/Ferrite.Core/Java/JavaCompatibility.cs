using Ferrite.Core.Minecraft;

namespace Ferrite.Core.Java;

public sealed record JavaCompatibilityResult(
    bool IsCompatible,
    int? RequiredMajor,
    int? ActualMajor,
    string? Message);

/// <summary>
/// Maps Minecraft generations to the Java major version they need, and judges a runtime against
/// that requirement.
/// </summary>
public static class JavaCompatibility
{
    /// <summary>Uses the version document's own requirement when present, otherwise the table.</summary>
    public static int? RequiredMajorFor(VersionDocument document, string versionId)
    {
        if (document.JavaVersion?.MajorVersion is { } explicitMajor and > 0)
        {
            return explicitMajor;
        }

        return RequiredMajorFor(versionId);
    }

    /// <summary>
    /// Fallback mapping for versions whose metadata does not declare a requirement. Boundaries
    /// follow the documented Java requirement of each release train.
    /// </summary>
    public static int? RequiredMajorFor(string versionId)
    {
        if (!TryParseReleaseVersion(versionId, out var release))
        {
            // Snapshots and unusual identifiers: assume a modern runtime.
            return 21;
        }

        if (release >= 26.0)
        {
            return 25;
        }

        if (release >= 1.20)
        {
            return 21;
        }

        if (release >= 1.18)
        {
            return 17;
        }

        if (release >= 1.17)
        {
            return 16;
        }

        return 8;
    }

    public static JavaCompatibilityResult Evaluate(JavaRuntime runtime, int? requiredMajor)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (requiredMajor is not { } required)
        {
            return new JavaCompatibilityResult(true, null, runtime.MajorVersion, null);
        }

        if (runtime.MajorVersion is not { } actual)
        {
            return new JavaCompatibilityResult(
                false,
                required,
                null,
                "The Java version could not be determined for this runtime.");
        }

        if (actual < required)
        {
            return new JavaCompatibilityResult(
                false,
                required,
                actual,
                $"This instance needs Java {required} or newer, but the selected runtime is Java {actual}.");
        }

        if (required <= 8 && actual >= 17)
        {
            return new JavaCompatibilityResult(
                true,
                required,
                actual,
                $"Minecraft {required} targets Java 8; Java {actual} usually works, but some older mods may not.");
        }

        return new JavaCompatibilityResult(true, required, actual, null);
    }

    /// <summary>Parses a release version such as "1.21.1", "26.3" or "1.7.10" into a comparable value.</summary>
    public static bool TryParseReleaseVersion(string versionId, out double release)
    {
        release = 0;
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return false;
        }

        var value = versionId.Trim();
        if (value.StartsWith('v'))
        {
            value = value[1..];
        }

        var parts = value.Split('-', '.');
        if (parts.Length < 2
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor))
        {
            return false;
        }

        var patch = 0;
        if (parts.Length >= 3)
        {
            var patchText = new string(parts[2].TakeWhile(char.IsDigit).ToArray());
            _ = int.TryParse(patchText, out patch);
        }

        if (major == 1)
        {
            release = 1d + (minor / 100d) + (patch / 10000d);
            return true;
        }

        // Year-stream versions (26.3) compare above every 1.x release.
        release = major + (minor / 100d);
        return true;
    }
}
