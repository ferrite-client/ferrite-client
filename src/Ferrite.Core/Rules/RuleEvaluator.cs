using System.Text.RegularExpressions;

namespace Ferrite.Core.Rules;

/// <summary>
/// Evaluates Mojang rule lists. Absent rules mean "allow"; otherwise the last matching rule wins,
/// which is the behaviour the launcher metadata relies on.
/// </summary>
public static class RuleEvaluator
{
    public static bool IsAllowed(IReadOnlyList<Rule>? rules, RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (Matches(rule, context))
            {
                allowed = rule.IsAllow;
            }
        }

        return allowed;
    }

    public static bool Matches(Rule rule, RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(context);

        if (rule.Os is { } os && !MatchesOs(os, context))
        {
            return false;
        }

        if (rule.Features is { Count: > 0 } features)
        {
            foreach (var (name, expected) in features)
            {
                var actual = context.Features.TryGetValue(name, out var value) && value;
                if (actual != expected)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool MatchesOs(OsRule os, RuleContext context)
    {
        if (!string.IsNullOrEmpty(os.Name)
            && !string.Equals(os.Name, context.MojangOsName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(os.Arch)
            && !string.Equals(os.Arch, context.MojangArchName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (os.VersionRange is { } range && !MatchesVersionRange(range, context.OsVersion))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(os.Version))
        {
            try
            {
                if (!Regex.IsMatch(context.OsVersion.ToString(), os.Version, RegexOptions.None, TimeSpan.FromSeconds(1)))
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                // An unparsable legacy pattern is treated as non-matching rather than fatal.
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesVersionRange(OsVersionRange range, Version version)
    {
        if (!string.IsNullOrEmpty(range.Min)
            && Version.TryParse(NormalizeVersion(range.Min), out var min)
            && Compare(version, min) < 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(range.Max)
            && Version.TryParse(NormalizeVersion(range.Max), out var max)
            && Compare(version, max) > 0)
        {
            return false;
        }

        return true;
    }

    private static int Compare(Version left, Version right)
    {
        var leftNormalized = new Version(left.Major, left.Minor, Math.Max(0, left.Build));
        var rightNormalized = new Version(right.Major, right.Minor, Math.Max(0, right.Build));
        return leftNormalized.CompareTo(rightNormalized);
    }

    private static string NormalizeVersion(string value)
    {
        // Ranges in the metadata are dotted numerics such as "10.0.17134"; keep them as-is.
        return value.Trim();
    }
}
