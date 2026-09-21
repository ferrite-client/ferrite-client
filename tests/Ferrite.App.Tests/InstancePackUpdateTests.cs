using System.IO.Compression;
using System.Text;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// Applying a pack over an instance that already exists. A failure has to leave the instance exactly
/// as it was, because the alternative is an instance pointing at content that was half replaced.
/// </summary>
public sealed class InstancePackUpdateTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly InstanceRecord _instance;
    private readonly InstanceDetailViewModel _viewModel;

    public InstancePackUpdateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-packupdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);

        _instance = _services.Instances
            .CreateAsync(
                new InstanceRecord
                {
                    Id = Guid.NewGuid(),
                    Name = "Existing pack",
                    MinecraftVersion = "1.21.1",
                    Loader = LoaderKind.Fabric,
                    LoaderVersion = "0.19.5",
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        _viewModel = new InstanceDetailViewModel(
            _instance,
            _services,
            new MainWindowViewModel(_services));
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
    public async Task An_archive_that_is_not_a_pack_leaves_the_instance_untouched()
    {
        var path = WriteArchive("notes.zip", archive => WriteEntry(archive, "readme.txt", "hello"));

        await _viewModel.UpdateFromArchiveAsync(path);

        Assert.Contains(
            "not a modpack",
            _viewModel.StatusNote ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        // The record on disk is exactly what it was: the update never started.
        var stored = (await _services.Instances.LoadAllAsync(TestContext.Current.CancellationToken))
            .Single(record => record.Id == _instance.Id);
        Assert.Equal("1.21.1", stored.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, stored.Loader);
        Assert.Equal("0.19.5", stored.LoaderVersion);
        Assert.Null(stored.Modpack);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_missing_file_is_reported_and_nothing_is_replaced()
    {
        await _viewModel.UpdateFromArchiveAsync(Path.Combine(_root, "absent.mrpack"));

        Assert.Equal(Localizer.Get("L.Library.UnreadableFile"), _viewModel.StatusNote);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_curseforge_pack_is_dispatched_to_the_curseforge_installer()
    {
        var path = WriteArchive("pack.zip", archive => WriteEntry(
            archive,
            "manifest.json",
            """
            {
              "minecraft": { "version": "1.20.1", "modLoaders": [ { "id": "forge-47.2.0", "primary": true } ] },
              "name": "Older pack",
              "version": "1.0.0",
              "files": [ { "projectID": 1, "fileID": 2, "required": true } ]
            }
            """));

        await _viewModel.UpdateFromArchiveAsync(path);

        // Recognised as a pack, and the refusal names the missing key rather than the archive.
        var status = _viewModel.StatusNote ?? string.Empty;
        Assert.DoesNotContain("not a modpack", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("key", status, StringComparison.OrdinalIgnoreCase);

        // Still the instance it was: no loader swap happened on a failed update.
        var stored = (await _services.Instances.LoadAllAsync(TestContext.Current.CancellationToken))
            .Single(record => record.Id == _instance.Id);
        Assert.Equal("1.21.1", stored.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, stored.Loader);
    }
}
