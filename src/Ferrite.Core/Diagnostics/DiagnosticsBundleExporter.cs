using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Ferrite.Core.Content;
using Ferrite.Core.Java;
using Ferrite.Core.Json;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Diagnostics;

public sealed record DiagnosticsBundleRequest
{
    public required string OutputPath { get; init; }

    /// <summary>Include this instance's logs, metadata, and crash reports.</summary>
    public InstanceRecord? Instance { get; init; }

    /// <summary>Free-text notes the user wants included, such as what they were doing.</summary>
    public string? Notes { get; init; }
}

public sealed record DiagnosticsBundleResult(
    string Path,
    int FileCount,
    long Bytes,
    IReadOnlyList<string> Entries);

/// <summary>
/// Builds a support bundle: launcher logs, operation history, instance metadata, game logs, and
/// crash reports, with credentials redacted. Every entry name is chosen by this class, so a hostile
/// file name in an instance folder cannot escape the archive.
/// </summary>
public sealed class DiagnosticsBundleExporter
{
    private const int MaxTextEntryBytes = 2 * 1024 * 1024;
    private const int MaxEntriesPerFolder = 20;

    private readonly AppPaths _paths;
    private readonly SecretRedactor _redactor;
    private readonly JavaDetector _java;
    private readonly InstanceContentManager _content;
    private readonly ILogger<DiagnosticsBundleExporter> _logger;

    public DiagnosticsBundleExporter(
        AppPaths paths,
        SecretRedactor redactor,
        JavaDetector java,
        InstanceContentManager content,
        ILogger<DiagnosticsBundleExporter> logger)
    {
        _paths = paths;
        _redactor = redactor;
        _java = java;
        _content = content;
        _logger = logger;
    }

    public async Task<DiagnosticsBundleResult> ExportAsync(
        DiagnosticsBundleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            throw new ArgumentException("An output path is required.", nameof(request));
        }

        var outputPath = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporary = outputPath + ".partial";
        AtomicFile.TryDelete(temporary);

        var entries = new List<string>();
        var count = 0;

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                count += AddText(archive, "system.txt", await BuildSystemReportAsync(request, cancellationToken)
                    .ConfigureAwait(false), entries);
                count += AddText(archive, "notes.txt", request.Notes, entries);
                count += AddFile(
                    archive,
                    "operations.jsonl",
                    Path.Combine(_paths.LauncherLogsDirectory, "operations.jsonl"),
                    entries);

                foreach (var log in EnumerateRecent(_paths.LauncherLogsDirectory, "*.log"))
                {
                    count += AddFile(archive, "launcher/" + Path.GetFileName(log), log, entries);
                }

                if (request.Instance is { } instance)
                {
                    count += AddText(
                        archive,
                        "instance/instance.json",
                        JsonSerializer.Serialize(instance, JsonDefaults.Document),
                        entries);

                    var gameDirectory = _paths.InstanceGameDirectory(instance.Id);
                    count += await AddCrashReportsAsync(archive, gameDirectory, instance, entries, cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var log in EnumerateRecent(Path.Combine(gameDirectory, "logs"), "*.log"))
                    {
                        count += AddFile(archive, "instance/logs/" + Path.GetFileName(log), log, entries);
                    }
                }
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        catch
        {
            AtomicFile.TryDelete(temporary);
            throw;
        }

        var info = new FileInfo(outputPath);
        _logger.LogInformation(
            "Diagnostics bundle written to {Path}: {Count} entries, {Bytes} bytes",
            outputPath,
            count,
            info.Length);

        return new DiagnosticsBundleResult(outputPath, count, info.Length, entries);
    }

    private async Task<string> BuildSystemReportAsync(
        DiagnosticsBundleRequest request,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ferrite diagnostics");
        builder.AppendLine("==================");
        builder.AppendLine($"Generated:          {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Launcher version:   {typeof(DiagnosticsBundleExporter).Assembly.GetName().Version}");
        builder.AppendLine($"Runtime:            {Environment.Version} ({Environment.OSVersion})");
        builder.AppendLine($"OS:                 {Environment.OSVersion.VersionString}, {Environment.OSVersion.Platform}");
        builder.AppendLine($"Architecture:       {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"Processors:         {Environment.ProcessorCount}");
        builder.AppendLine($"Data root:          {_paths.Root}");
        builder.AppendLine($"64-bit process:     {Environment.Is64BitProcess}");
        builder.AppendLine();

        builder.AppendLine("Java runtimes");
        builder.AppendLine("-------------");
        try
        {
            var runtimes = await _java.DetectAsync(cancellationToken).ConfigureAwait(false);
            if (runtimes.Count == 0)
            {
                builder.AppendLine("(none detected)");
            }

            foreach (var runtime in runtimes)
            {
                builder.AppendLine(
                    $"  {runtime.ExecutablePath} - {runtime.DisplayName}");
            }
        }
        catch (Exception exception)
        {
            builder.AppendLine($"  detection failed: {exception.Message}");
        }

        builder.AppendLine();
        builder.AppendLine("Instance");
        builder.AppendLine("--------");
        if (request.Instance is { } instance)
        {
            builder.AppendLine($"  Name:            {instance.Name}");
            builder.AppendLine($"  Minecraft:       {instance.MinecraftVersion}");
            builder.AppendLine($"  Loader:          {instance.Loader.ToDisplayName()} {instance.LoaderVersion ?? "-"}");
            builder.AppendLine($"  Java override:   {instance.JavaPath ?? "(automatic)"}");
            builder.AppendLine($"  Memory:          {instance.MemoryMb?.ToString() ?? "(default)"} MiB");
            builder.AppendLine($"  Modpack:         {instance.Modpack?.Provider} {instance.Modpack?.VersionName}");
        }
        else
        {
            builder.AppendLine("  (launcher-wide bundle)");
        }

        return builder.ToString();
    }

    private async Task<int> AddCrashReportsAsync(
        ZipArchive archive,
        string gameDirectory,
        InstanceRecord instance,
        List<string> entries,
        CancellationToken cancellationToken)
    {
        var crashDirectory = Path.Combine(gameDirectory, "crash-reports");
        var count = 0;
        foreach (var report in EnumerateRecent(crashDirectory, "*.txt"))
        {
            var text = TryReadText(report);
            if (text is null)
            {
                continue;
            }

            count += AddText(archive, "instance/crash-reports/" + Path.GetFileName(report), text, entries);
        }

        // The newest report gets an analysis, which is what a support reader actually wants.
        var newest = EnumerateRecent(crashDirectory, "*.txt").FirstOrDefault();
        if (newest is null)
        {
            return count;
        }

        var raw = TryReadText(newest);
        if (raw is null || !CrashReportParser.LooksLikeCrashReport(raw))
        {
            return count;
        }

        var report2 = CrashReportParser.Parse(newest, raw);
        IReadOnlyList<ModMetadata> mods;
        try
        {
            mods = await _content.ListModsAsync(gameDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            mods = [];
        }

        var analysis = CrashAnalyzer.Analyze(report2, mods);
        count += AddText(
            archive,
            "instance/crash-analysis.txt",
            CrashAnalysisText.Render(analysis, instance),
            entries);
        return count;
    }

    private int AddText(ZipArchive archive, string entryName, string? text, List<string> entries)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var redacted = _redactor.Redact(text);
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(redacted);
        entries.Add(entryName);
        return 1;
    }

    private int AddFile(ZipArchive archive, string entryName, string path, List<string> entries)
    {
        var text = TryReadText(path);
        return text is null ? 0 : AddText(archive, entryName, text, entries);
    }

    private static string? TryReadText(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[MaxTextEntryBytes];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, read);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Newest first, so a capped bundle keeps the files that matter.</summary>
    private static IEnumerable<string> EnumerateRecent(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(directory, pattern)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(MaxEntriesPerFolder)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }
}
