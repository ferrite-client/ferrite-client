using System.Text;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Diagnostics;

/// <summary>
/// Renders an advisor report as plain text. Kept in Core so the instance page and the diagnostics
/// bundle show exactly the same thing.
/// </summary>
public static class AdvisorText
{
    public static string Render(AdvisorReport report, InstanceRecord? instance = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder();

        builder.AppendLine("Instance assistant");
        builder.AppendLine("==================");
        if (instance is not null)
        {
            builder.AppendLine(
                $"Instance: Minecraft {instance.MinecraftVersion}, "
                + $"{instance.Loader.ToDisplayName()} {instance.LoaderVersion ?? "-"}");
        }

        builder.AppendLine(report.HasProblems
            ? $"Found {report.Findings.Count(finding => finding.Severity >= AdvisorSeverity.Medium)} "
              + "problem(s) worth acting on."
            : "No blocking problem was found.");
        builder.AppendLine();

        foreach (var finding in report.Findings)
        {
            builder.AppendLine($"[{Label(finding.Severity)}] {finding.Title}");
            builder.AppendLine($"  What was found: {finding.Detail}");
            builder.AppendLine($"  What to do:     {finding.Action}");
            builder.AppendLine();
        }

        builder.AppendLine(
            "This assistant reads this instance's own crash reports, log, mod metadata, and last "
            + "preflight result. It reports evidence, not a verdict, and it does not call out to any "
            + "external service.");
        return builder.ToString();
    }

    private static string Label(AdvisorSeverity severity) => severity switch
    {
        AdvisorSeverity.High => "BLOCKING",
        AdvisorSeverity.Medium => "LIKELY",
        AdvisorSeverity.Low => "NOTE",
        _ => "CONTEXT",
    };
}
