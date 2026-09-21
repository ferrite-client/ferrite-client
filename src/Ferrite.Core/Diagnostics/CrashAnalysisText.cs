using System.Text;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Diagnostics;

/// <summary>
/// Renders a crash analysis as plain text. It is used for the exported bundle and for the instance
/// detail page, so the two can never disagree about what was found.
/// </summary>
public static class CrashAnalysisText
{
    public static string Render(CrashAnalysis analysis, InstanceRecord? instance = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var report = analysis.Report;
        var builder = new StringBuilder();

        builder.AppendLine("Crash report");
        builder.AppendLine("============");
        builder.AppendLine($"File:        {report.FileName}");
        if (instance is not null)
        {
            builder.AppendLine($"Instance:    {instance.Name} ({instance.MinecraftVersion}, "
                + $"{instance.Loader.ToDisplayName()} {instance.LoaderVersion ?? "-"})");
        }

        builder.AppendLine($"Time:        {(report.Time is { } time ? time.ToString("yyyy-MM-dd HH:mm:ss") : "(not stated)")}");
        builder.AppendLine($"Description: {report.Description ?? "(not stated)"}");
        builder.AppendLine($"Cause:       {report.Summary}");

        var reportedVersion = report.SystemDetails.TryGetValue("Minecraft Version", out var version)
            ? version
            : null;
        if (reportedVersion is not null)
        {
            builder.AppendLine($"Game version in report: {reportedVersion}");
        }

        builder.AppendLine();
        builder.AppendLine("Mods named by the report");
        builder.AppendLine("------------------------");
        AppendList(builder, analysis.SuspectedByReport, "The report names no mods as suspects.");

        builder.AppendLine();
        builder.AppendLine("Mods referenced by the stack trace");
        builder.AppendLine("----------------------------------");
        if (analysis.AttributedMods.Count == 0)
        {
            builder.AppendLine("No installed mod's classes appear in the stack trace.");
        }
        else
        {
            foreach (var attribution in analysis.AttributedMods)
            {
                builder.AppendLine($"  {attribution.DisplayName} [{attribution.ModId}] - {attribution.Evidence}");
                foreach (var frame in attribution.Frames)
                {
                    builder.AppendLine($"      at {frame}");
                }
            }
        }

        builder.AppendLine(
            "(A frame is evidence that code ran, not proof of cause: treat this as a starting point, "
            + "not a verdict.)");

        builder.AppendLine();
        builder.AppendLine("Listed in the report but not installed now");
        builder.AppendLine("------------------------------------------");
        AppendList(builder, analysis.MissingMods, "Every mod in the report is still installed.");

        if (report.Frames.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Stack trace ({report.Frames.Count} frame(s))");
            builder.AppendLine("-------------------");
            foreach (var frame in report.Frames.Take(40))
            {
                builder.AppendLine($"  at {frame.DisplayText}");
            }

            if (report.Frames.Count > 40)
            {
                builder.AppendLine($"  ... {report.Frames.Count - 40} more frame(s)");
            }
        }

        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, IReadOnlyList<string> values, string emptyText)
    {
        if (values.Count == 0)
        {
            builder.AppendLine(emptyText);
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine($"  {value}");
        }
    }
}
