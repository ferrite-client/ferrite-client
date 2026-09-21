using System.IO.Compression;
using System.Text;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// Importing a pack archive, which is what both the import button and a dropped file call.
/// </summary>
public sealed class LibraryImportTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly LibraryViewModel _viewModel;

    public LibraryImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
        _viewModel = new LibraryViewModel(_services, new MainWindowViewModel(_services));
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteArchive(string name, Action<ZipArchive> fill)
    {
        var path = Path.Combine(_root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        fill(archive);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string contents)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_dropped_archive_that_is_not_a_pack_says_so()
    {
        var path = WriteArchive("notes.zip", archive => WriteEntry(archive, "notes.txt", "hello"));

        await _viewModel.ImportModpackAsync(path);

        Assert.Contains(
            "not a modpack",
            _viewModel.ModpackStatus ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_dropped_curseforge_pack_is_dispatched_to_its_installer()
    {
        // A CurseForge pack declares itself with a manifest.json at the archive root.
        var path = WriteArchive("curseforge-pack.zip", archive => WriteEntry(
            archive,
            "manifest.json",
            """
            {
              "minecraft": { "version": "1.21.1", "modLoaders": [ { "id": "fabric-0.19.5", "primary": true } ] },
              "name": "Fixture pack",
              "version": "1.0.0",
              "files": [ { "projectID": 238222, "fileID": 5512345, "required": true } ]
            }
            """));

        await _viewModel.ImportModpackAsync(path);

        // It is recognised as a pack - the failure is about the missing API key, which is what
        // resolving a CurseForge file needs, not about the archive being unsupported.
        var status = _viewModel.ModpackStatus ?? string.Empty;
        Assert.DoesNotContain("not a modpack", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("key", status, StringComparison.OrdinalIgnoreCase);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_missing_file_is_reported_rather_than_ignored()
    {
        await _viewModel.ImportModpackAsync(Path.Combine(_root, "does-not-exist.mrpack"));

        Assert.Equal(Localizer.Get("L.Library.UnreadableFile"), _viewModel.ModpackStatus);
    }
}
