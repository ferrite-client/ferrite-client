using Ferrite.Core.Platform;
using Ferrite.Core.Rules;

namespace Ferrite.Core.Tests;

public sealed class RuleEvaluatorTests
{
    private static RuleContext Context(
        OperatingSystemKind os = OperatingSystemKind.Windows,
        Version? version = null,
        IReadOnlyDictionary<string, bool>? features = null) =>
        new()
        {
            Os = os,
            MojangOsName = os switch
            {
                OperatingSystemKind.Windows => "windows",
                OperatingSystemKind.Linux => "linux",
                _ => "osx",
            },
            MojangArchName = "x86_64",
            OsVersion = version ?? new Version(10, 0, 22631),
            Features = features ?? new Dictionary<string, bool>(),
        };

    [Fact]
    public void Missing_rules_allow()
    {
        Assert.True(RuleEvaluator.IsAllowed(null, Context()));
        Assert.True(RuleEvaluator.IsAllowed([], Context()));
    }

    [Fact]
    public void Allow_rule_for_a_different_os_does_not_match()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new OsRule { Name = "osx" } },
        };

        Assert.False(RuleEvaluator.IsAllowed(rules, Context()));
        Assert.True(RuleEvaluator.IsAllowed(rules, Context(OperatingSystemKind.MacOs)));
    }

    [Fact]
    public void Last_matching_rule_wins()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new OsRule { Name = "osx" } },
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, Context()));
        Assert.False(RuleEvaluator.IsAllowed(rules, Context(OperatingSystemKind.MacOs)));
    }

    [Fact]
    public void Version_range_min_is_respected()
    {
        var rules = new List<Rule>
        {
            new()
            {
                Action = "allow",
                Os = new OsRule
                {
                    Name = "windows",
                    VersionRange = new OsVersionRange { Min = "10.0.17134" },
                },
            },
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, Context(version: new Version(10, 0, 22631))));
        Assert.False(RuleEvaluator.IsAllowed(rules, Context(version: new Version(10, 0, 15000))));
    }

    [Fact]
    public void Version_range_max_is_respected()
    {
        var rules = new List<Rule>
        {
            new()
            {
                Action = "allow",
                Os = new OsRule
                {
                    Name = "windows",
                    VersionRange = new OsVersionRange { Max = "10.0.17134" },
                },
            },
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, Context(version: new Version(10, 0, 15000))));
        Assert.False(RuleEvaluator.IsAllowed(rules, Context(version: new Version(10, 0, 22631))));
    }

    [Fact]
    public void Feature_rules_require_the_feature_to_be_enabled()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Features = new Dictionary<string, bool> { ["is_demo_user"] = true } },
        };

        Assert.True(RuleEvaluator.IsAllowed(
            rules,
            Context(features: new Dictionary<string, bool> { ["is_demo_user"] = true })));
        Assert.False(RuleEvaluator.IsAllowed(rules, Context()));
    }

    [Fact]
    public void Feature_rules_can_require_a_feature_to_be_disabled()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Features = new Dictionary<string, bool> { ["has_custom_resolution"] = false } },
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, Context()));
        Assert.False(RuleEvaluator.IsAllowed(
            rules,
            Context(features: new Dictionary<string, bool> { ["has_custom_resolution"] = true })));
    }

    [Fact]
    public void Architecture_rule_is_respected()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Os = new OsRule { Arch = "arm64" } },
        };

        Assert.False(RuleEvaluator.IsAllowed(rules, Context()));
    }
}
