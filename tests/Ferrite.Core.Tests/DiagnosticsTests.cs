using System.IO.Compression;
using System.Text.Json;
using Ferrite.Core.Content;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Java;
using Ferrite.Core.Json;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>Operation history and the support bundle a user is asked to send.</summary>
public sealed class DiagnosticsTests : IDisposable
{
    private const string Secret = "super-secret-token-value-1234";

    private readonly string _root;
    private readonly AppPaths _paths;

    public DiagnosticsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-diag-" + Guid.NewGuid().ToString("N"));
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void An_operation_entry_round_trips_through_json()
    {
        var entry = new OperationEntry(
            "Installing Minecraft",
            OperationOutcome.Failed,
            DateTimeOffset.Parse("2026-09-21T10:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            TimeSpan.FromSeconds(2),
            "detail");

        var json = JsonSerializer.Serialize(entry, JsonDefaults.Document);
        var restored = JsonSerializer.Deserialize<OperationEntry>(json, JsonDefaults.Document);

        Assert.NotNull(restored);
        Assert.Equal(entry.Operation, restored!.Operation);
        Assert.Equal(OperationOutcome.Failed, restored.Outcome);
        Assert.Equal(entry.Duration, restored.Duration);
    }

    [Fact]
    public async Task Operations_are_recorded_and_read_back_newest_first()
    {
        var log = CreateOperationLog();
        using (var scope = log.Begin("Installing Minecraft 1.21.1"))
        {
            scope.Note("done");
        }

        using (var scope = log.Begin("Launching instance"))
        {
            scope.Fail("java not found");
        }

        var recent = log.Recent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal("Launching instance", recent[0].Operation);
        Assert.Equal(OperationOutcome.Failed, recent[0].Outcome);
        Assert.Equal("java not found", recent[0].Detail);
        Assert.Equal(OperationOutcome.Succeeded, recent[1].Outcome);

        var reloaded = CreateOperationLog();
        await reloaded.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, reloaded.Recent(10).Count);
        Assert.Equal("Launching instance", reloaded.Recent(10)[0].Operation);
    }

    [Fact]
    public void The_history_is_bounded()
    {
        var log = CreateOperationLog(capacity: 3);
        for (var index = 0; index < 10; index++)
        {
            log.Record($"operation {index}", OperationOutcome.Succeeded, null, TimeSpan.FromSeconds(1));
        }

        var recent = log.Recent(10);
        Assert.Equal(3, recent.Count);
        Assert.Equal("operation 9", recent[0].Operation);
        Assert.Equal("operation 7", recent[2].Operation);
    }

    [Fact]
    public async Task The_history_file_holds_one_entry_per_line()
    {
        var log = CreateOperationLog();
        using (log.Begin("first operation"))
        {
        }

        using (log.Begin("second operation"))
        {
        }

        var lines = await File.ReadAllLinesAsync(
            Path.Combine(_paths.LauncherLogsDirectory, "operations.jsonl"),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            Assert.StartsWith("{", line, StringComparison.Ordinal);
            Assert.EndsWith("}", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_damaged_history_line_does_not_break_loading()
    {
        var file = Path.Combine(_paths.LauncherLogsDirectory, "operations.jsonl");
        await File.WriteAllTextAsync(
            file,
            """
            {"operation":"good one","outcome":0,"startedAt":"2026-09-21T10:00:00+00:00","duration":"00:00:02","detail":null}
            not json at all
            """,
            TestContext.Current.CancellationToken);

        var log = CreateOperationLog();
        await log.LoadAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(log.Recent(5));
        Assert.Equal("good one", entry.Operation);
    }

    [Fact]
    public async Task A_bundle_contains_logs_metadata_and_a_crash_analysis_with_secrets_removed()
    {
        var secrets = new SecretRedactor();
        secrets.Register(Secret);

        await File.WriteAllTextAsync(
            Path.Combine(_paths.LauncherLogsDirectory, "ferrite.log"),
            $"{{\"level\":\"info\",\"message\":\"token={Secret}\"}}",
            TestContext.Current.CancellationToken);

        var instance = await new InstanceStore(_paths, NullLogger<InstanceStore>.Instance)
            .CreateAsync(
                new InstanceRecord { Id = Guid.NewGuid(), Name = "Diag instance", MinecraftVersion = "1.21.1" },
                TestContext.Current.CancellationToken);

        var gameDirectory = _paths.InstanceGameDirectory(instance.Id);
        Directory.CreateDirectory(Path.Combine(gameDirectory, "logs"));
        Directory.CreateDirectory(Path.Combine(gameDirectory, "crash-reports"));
        await File.WriteAllTextAsync(
            Path.Combine(gameDirectory, "logs", "latest.log"),
            "[12:00:00] [main/INFO]: Loading Minecraft 1.21.1",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(gameDirectory, "crash-reports", "crash-2026-09-20_21.14.03-client.txt"),
            """
            ---- Minecraft Crash Report ----
            // Don't be sad, have a hug! <3

            Time: 2026-09-20 21:14:03
            Description: Rendering overlay

            java.lang.NullPointerException: boom
            	at net.example.mymod.Renderer.draw(Renderer.java:10)

            -- System Details --
            Details:
            	Minecraft Version: 1.21.1
            	Suspected Mods: mymod
            """,
            TestContext.Current.CancellationToken);

        var outputPath = Path.Combine(_root, "bundle.zip");
        var exporter = new DiagnosticsBundleExporter(
            _paths,
            secrets,
            new JavaDetector(_paths, NullLogger<JavaDetector>.Instance),
            new InstanceContentManager(new ModScanner(NullLogger<ModScanner>.Instance)),
            NullLogger<DiagnosticsBundleExporter>.Instance);

        var result = await exporter.ExportAsync(
            new DiagnosticsBundleRequest
            {
                OutputPath = outputPath,
                Instance = instance,
                Notes = "it crashed when opening the world",
            },
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outputPath));
        Assert.Contains("system.txt", result.Entries);
        Assert.Contains("notes.txt", result.Entries);
        Assert.Contains("launcher/ferrite.log", result.Entries);
        Assert.Contains("instance/instance.json", result.Entries);
        Assert.Contains("instance/logs/latest.log", result.Entries);
        Assert.Contains("instance/crash-reports/crash-2026-09-20_21.14.03-client.txt", result.Entries);
        Assert.Contains("instance/crash-analysis.txt", result.Entries);

        using var archive = ZipFile.OpenRead(outputPath);
        var system = ReadEntry(archive, "system.txt");
        Assert.Contains("Diag instance", system, StringComparison.Ordinal);
        Assert.Contains("1.21.1", system, StringComparison.Ordinal);

        var analysis = ReadEntry(archive, "instance/crash-analysis.txt");
        Assert.Contains("Rendering overlay", analysis, StringComparison.Ordinal);
        Assert.Contains("mymod", analysis, StringComparison.Ordinal);

        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
            Assert.DoesNotContain("..", entry.FullName, StringComparison.Ordinal);
        }
    }

    private OperationLog CreateOperationLog(int capacity = 200) => new(
        Path.Combine(_paths.LauncherLogsDirectory, "operations.jsonl"),
        NullLogger<OperationLog>.Instance,
        capacity);

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }
}
