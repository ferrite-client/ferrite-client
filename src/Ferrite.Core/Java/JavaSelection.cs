namespace Ferrite.Core.Java;

/// <summary>
/// Chooses the best runtime for a requirement. The closest supported version wins over the newest
/// one: running an older Minecraft or loader on a much newer JVM is a common source of mixin and
/// classloader failures, so an exact match is preferred, then the smallest version that satisfies
/// the requirement, and only then anything newer.
/// </summary>
public static class JavaSelection
{
    public static JavaRuntime? SelectBest(IReadOnlyList<JavaRuntime> runtimes, int? requiredMajor)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        if (runtimes.Count == 0)
        {
            return null;
        }

        var usable = runtimes
            .Where(runtime => runtime.MajorVersion is not null)
            .Where(runtime => JavaCompatibility.Evaluate(runtime, requiredMajor).IsCompatible)
            .ToList();

        if (usable.Count == 0)
        {
            return null;
        }

        if (requiredMajor is not { } required)
        {
            return usable.OrderByDescending(runtime => runtime.MajorVersion).First();
        }

        var exact = usable.FirstOrDefault(runtime => runtime.MajorVersion == required);
        if (exact is not null)
        {
            return exact;
        }

        var closestAbove = usable
            .Where(runtime => runtime.MajorVersion > required)
            .OrderBy(runtime => runtime.MajorVersion)
            .FirstOrDefault();

        return closestAbove ?? usable.OrderByDescending(runtime => runtime.MajorVersion).First();
    }

    /// <summary>Preference order for showing runtimes in a picker: usable first, closest first.</summary>
    public static IReadOnlyList<JavaRuntime> Rank(IReadOnlyList<JavaRuntime> runtimes, int? requiredMajor) =>
        runtimes
            .OrderByDescending(runtime => JavaCompatibility.Evaluate(runtime, requiredMajor).IsCompatible)
            .ThenBy(runtime => requiredMajor is { } required && runtime.MajorVersion is { } major
                ? Math.Abs(major - required)
                : 0)
            .ThenByDescending(runtime => runtime.MajorVersion ?? 0)
            .ToList();
}
