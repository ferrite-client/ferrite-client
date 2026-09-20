using Ferrite.Core.Java;

namespace Ferrite.Core.Minecraft;

public sealed record PreflightIssue(string Code, string Message, bool IsBlocking);

/// <summary>
/// Checks that a launch can actually succeed before a process is started, so the user sees a
/// specific problem instead of a Java stack trace.
/// </summary>
public static class LaunchPreflight
{
    public static IReadOnlyList<PreflightIssue> Check(LaunchRequest request, LaunchCommand command)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(command);

        var issues = new List<PreflightIssue>();

        if (!File.Exists(request.Java.ExecutablePath))
        {
            issues.Add(new PreflightIssue(
                "java-missing",
                "The selected Java runtime no longer exists at its recorded path.",
                IsBlocking: true));
        }
        else
        {
            var required = JavaCompatibility.RequiredMajorFor(request.Document, request.Plan.VersionId);
            var compatibility = JavaCompatibility.Evaluate(request.Java, required);
            if (!compatibility.IsCompatible)
            {
                issues.Add(new PreflightIssue(
                    "java-incompatible",
                    compatibility.Message ?? "The selected Java runtime is not compatible with this version.",
                    IsBlocking: true));
            }
            else if (compatibility.Message is { Length: > 0 } warning)
            {
                issues.Add(new PreflightIssue("java-warning", warning, IsBlocking: false));
            }
        }

        if (request.Plan.ClientJarPath is { } clientJar && !File.Exists(clientJar))
        {
            issues.Add(new PreflightIssue(
                "client-missing",
                "The Minecraft client jar is missing. Run repair on this instance.",
                IsBlocking: true));
        }

        var missingLibraries = request.Plan.Libraries.Count(library => !File.Exists(library.TargetPath));
        if (missingLibraries > 0)
        {
            issues.Add(new PreflightIssue(
                "libraries-missing",
                $"{missingLibraries} library file(s) are missing. Run repair on this instance.",
                IsBlocking: true));
        }

        if (!Directory.Exists(request.NativesDirectory))
        {
            issues.Add(new PreflightIssue(
                "natives-missing",
                "The natives directory is missing. Run repair on this instance.",
                IsBlocking: true));
        }

        if (string.IsNullOrWhiteSpace(request.Account.AccessToken))
        {
            issues.Add(new PreflightIssue(
                "account-token",
                "No account token is available; the game will start but cannot join online servers.",
                IsBlocking: false));
        }

        var memoryMb = request.Instance.MemoryMb ?? request.DefaultMemoryMb;
        if (GetAvailableMemoryMb() is { } available && memoryMb > available)
        {
            issues.Add(new PreflightIssue(
                "memory-too-high",
                $"The instance is configured for {memoryMb} MB, but only about {available} MB is available.",
                IsBlocking: false));
        }

        if (memoryMb < 512)
        {
            issues.Add(new PreflightIssue(
                "memory-too-low",
                "Less than 512 MB of heap will almost certainly fail to start.",
                IsBlocking: true));
        }

        return issues;
    }

    public static long? GetAvailableMemoryMb()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
            {
                return info.TotalAvailableMemoryBytes / (1024 * 1024);
            }
        }
        catch (NotSupportedException)
        {
        }

        return null;
    }

    /// <summary>A conservative default heap based on the machine's available memory.</summary>
    public static int SuggestDefaultMemoryMb()
    {
        if (GetAvailableMemoryMb() is not { } total)
        {
            return 4096;
        }

        return (int)Math.Clamp(total / 4, 2048, 8192);
    }
}
