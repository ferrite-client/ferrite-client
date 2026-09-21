using System.Text.Json;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// The launcher's own log file and its settings document, both against real files: one JSON object
/// per line with secrets removed, rotation that keeps the older file, and a settings document that is
/// damaged or from the future without taking startup down.
/// </summary>
public sealed class LauncherDiagnosticsTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;

    public LauncherDiagnosticsTests()
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
    public void The_log_file_is_json_lines_with_secrets_removed()
    {
        var directory = Path.Combine(_root, "logs");
        var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = directory });
        var logger = provider.CreateLogger("Ferrite.Test");

        logger.LogInformation("Downloading {Url} for instance {Instance}", "https://example.invalid/a.jar", "Test");
        logger.LogWarning("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.secretpart.signaturepart");
        provider.Dispose();

        var path = Path.Combine(directory, "ferrite.log");
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);

        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("info", first.RootElement.GetProperty("level").GetString());
        Assert.Equal("Ferrite.Test", first.RootElement.GetProperty("category").GetString());
        Assert.Contains("a.jar", first.RootElement.GetProperty("message").GetString()!, StringComparison.Ordinal);

        // A bearer token must not survive into the log, whatever it was logged as.
        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", raw, StringComparison.Ordinal);
        Assert.Contains("***", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void A_log_that_reaches_its_limit_rotates_and_keeps_a_previous_file()
    {
        var directory = Path.Combine(_root, "logs-rotated");
        var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = directory,
            MaxFileBytes = 512,
            RetainedFiles = 2,
        });
        var logger = provider.CreateLogger("Ferrite.Test");

        for (var index = 0; index < 40; index++)
        {
            logger.LogInformation("Line {Index} {Padding}", index, new string('x', 64));
        }

        provider.Dispose();

        var files = Directory.GetFiles(directory).Select(Path.GetFileName).ToList();
        Assert.Contains("ferrite.log", files);
        // Rotation named the previous file rather than truncating the current one.
        Assert.Contains(files, name => name!.Contains(".1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Damaged_settings_are_preserved_and_startup_uses_defaults()
    {
        var store = new SettingsStore(_paths, NullLogger<SettingsStore>.Instance);
        await store.SaveAsync(TestContext.Current.CancellationToken);
        store.Current.MaxConcurrentDownloads = 3;
        await store.SaveAsync(TestContext.Current.CancellationToken);

        // Corrupt the document the way a half-written file would be.
        await File.WriteAllTextAsync(
            _paths.SettingsFile,
            """{ "schemaVersion": 1, "maxConcurrentDownloads": """,
            TestContext.Current.CancellationToken);

        var reloaded = new SettingsStore(_paths, NullLogger<SettingsStore>.Instance);
        var settings = await reloaded.LoadAsync(TestContext.Current.CancellationToken);

        // Defaults, not a crash and not a partially parsed document.
        Assert.Equal(8, settings.MaxConcurrentDownloads);
        var preserved = Directory.EnumerateFiles(_paths.BackupsDirectory, "settings-*.json").ToList();
        Assert.Single(preserved);
        Assert.Contains(
            "maxConcurrentDownloads",
            await File.ReadAllTextAsync(preserved[0], TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_from_a_newer_schema_are_backed_up_rather_than_mangled()
    {
        await File.WriteAllTextAsync(
            _paths.SettingsFile,
            $$"""{ "schemaVersion": {{LauncherSettings.CurrentSchemaVersion + 1}}, "maxConcurrentDownloads": 2 }""",
            TestContext.Current.CancellationToken);

        var store = new SettingsStore(_paths, NullLogger<SettingsStore>.Instance);
        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(8, settings.MaxConcurrentDownloads);
        Assert.Single(Directory.EnumerateFiles(_paths.BackupsDirectory, "settings-*.json"));
        // The file itself is left as the newer build wrote it.
        Assert.Contains(
            (LauncherSettings.CurrentSchemaVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            await File.ReadAllTextAsync(_paths.SettingsFile, TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_older_settings_document_is_migrated_and_saved_forward()
    {
        await File.WriteAllTextAsync(
            _paths.SettingsFile,
            """{ "maxConcurrentDownloads": 0, "showSnapshotsInVersionList": true }""",
            TestContext.Current.CancellationToken);

        var store = new SettingsStore(_paths, NullLogger<SettingsStore>.Instance);
        var settings = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LauncherSettings.CurrentSchemaVersion, settings.SchemaVersion);
        // An out-of-range value is corrected rather than carried into the download engine.
        Assert.Equal(8, settings.MaxConcurrentDownloads);
        Assert.True(settings.ShowSnapshotsInVersionList);

        var written = await File.ReadAllTextAsync(_paths.SettingsFile, TestContext.Current.CancellationToken);
        Assert.Contains(
            $"\"schemaVersion\": {LauncherSettings.CurrentSchemaVersion}",
            written,
            StringComparison.Ordinal);
    }
}
