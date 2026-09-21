using Ferrite.Core.Content;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Tests;

/// <summary>
/// The rule-based assistant. Every finding is a rule over evidence the instance already holds, so the
/// tests assert the rules rather than a rendering.
/// </summary>
public sealed class InstanceAdvisorTests
{
    private static InstanceRecord Instance() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        MinecraftVersion = "1.21.1",
        Loader = LoaderKind.Fabric,
        LoaderVersion = "0.19.5",
    };

    private static ModMetadata Mod(
        string id,
        string? name = null,
        IReadOnlyList<string>? dependencies = null,
        bool enabled = true) => new()
    {
        FilePath = $"/mods/{id}.jar",
        FileName = $"{id}.jar",
        ModId = id,
        Name = name ?? id,
        Enabled = enabled,
        Dependencies = dependencies ?? [],
    };

    private static CrashFrame Frame(string className) =>
        new(className, "run", "Some.java", 12);

    [Fact]
    public void A_healthy_instance_produces_one_context_finding()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            Mods = [Mod("sodium")],
        });

        var finding = Assert.Single(report.Findings);
        Assert.Equal("clean", finding.Code);
        Assert.Equal(AdvisorSeverity.Info, finding.Severity);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void Missing_java_is_a_blocking_finding()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            JavaAvailable = false,
        });

        var finding = Assert.Single(report.Findings, item => item.Code == "java-none");
        Assert.Equal(AdvisorSeverity.High, finding.Severity);
        Assert.True(report.HasProblems);
    }

    [Fact]
    public void A_blocking_preflight_issue_becomes_a_finding_with_the_matching_action()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            Preflight =
            [
                new PreflightIssue("client-missing", "The Minecraft client jar is missing.", IsBlocking: true),
                // A warning is not a reason to tell the user anything went wrong.
                new PreflightIssue("java-warning", "A newer Java would be better.", IsBlocking: false),
            ],
        });

        var finding = Assert.Single(report.Findings);
        Assert.Equal("preflight-client-missing", finding.Code);
        Assert.Contains("Repair", finding.Action, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stack_frame_that_names_an_installed_mod_is_reported_as_evidence()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            Mods = [Mod("sodium", "Sodium")],
            CrashReports =
            [
                new CrashReport
                {
                    Path = "/crash-reports/crash-1.txt",
                    ExceptionType = "java.lang.NullPointerException",
                    Frames = [Frame("net.caffeinemc.mods.sodium.SodiumClientMod")],
                },
            ],
        });

        var finding = Assert.Single(report.Findings, item => item.Code == "crash-frames");
        Assert.Equal(AdvisorSeverity.Medium, finding.Severity);
        Assert.Contains("Sodium", finding.Detail, StringComparison.Ordinal);
        // The wording must not present a frame as proof.
        Assert.Contains("not proof", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mod_in_the_crash_report_that_is_no_longer_installed_is_blocking()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            Mods = [Mod("sodium")],
            CrashReports =
            [
                new CrashReport
                {
                    Path = "/crash-reports/crash-1.txt",
                    ExceptionType = "java.lang.NoSuchMethodError",
                    ListedMods = ["sodium: Sodium 1.0", "lithium: Lithium 2.0"],
                },
            ],
        });

        var finding = Assert.Single(report.Findings, item => item.Code == "crash-missing-mods");
        Assert.Equal(AdvisorSeverity.High, finding.Severity);
        Assert.Contains("lithium", finding.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_required_dependency_that_is_absent_is_reported_and_provided_ids_are_not()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            Mods =
            [
                Mod("sodium", "Sodium", ["fabric-api", "fabricloader", "cloth-config"]),
            ],
        });

        var finding = Assert.Single(report.Findings);
        Assert.Equal("mod-missing-dependency", finding.Code);
        Assert.Contains("cloth-config", finding.Detail, StringComparison.OrdinalIgnoreCase);
        // fabric-api and fabricloader are provided by the loader, so they are not "missing".
        Assert.DoesNotContain("fabric-api", finding.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fabricloader", finding.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Known_log_signatures_become_findings_with_the_log_line_as_evidence()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            LogText =
                "[12:00:00] [main/INFO]: Loading Minecraft 1.21.1\n"
                + "[12:00:10] [main/ERROR]: java.lang.OutOfMemoryError: Java heap space\n",
        });

        var finding = Assert.Single(report.Findings, item => item.Code == "log-out-of-memory");
        Assert.Equal(AdvisorSeverity.High, finding.Severity);
        Assert.Contains("OutOfMemoryError", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_mods_are_context_not_a_problem()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            Mods = [Mod("sodium"), Mod("lithium", enabled: false)],
        });

        var finding = Assert.Single(report.Findings);
        Assert.Equal("mods-disabled", finding.Code);
        Assert.Equal(AdvisorSeverity.Info, finding.Severity);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void Findings_are_ordered_worst_first()
    {
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = Instance(),
            JavaAvailable = false,
            Mods = [Mod("sodium", "Sodium", ["cloth-config"], enabled: false)],
            LogText = "Duplicate mod: sodium",
        });

        var severities = report.Findings.Select(finding => finding.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(severity => severity).ToList(), severities);
        Assert.Equal(AdvisorSeverity.High, report.Findings[0].Severity);
    }

    [Fact]
    public void The_rendering_mentions_the_findings_and_says_it_is_rule_based()
    {
        var instance = Instance();
        var report = InstanceAdvisor.Advise(new AdvisorInputs
        {
            Instance = instance,
            JavaAvailable = false,
        });

        var text = AdvisorText.Render(report, instance);

        Assert.Contains("Instance assistant", text, StringComparison.Ordinal);
        Assert.Contains("No Java runtime was found", text, StringComparison.Ordinal);
        Assert.Contains("external service", text, StringComparison.Ordinal);
    }
}
