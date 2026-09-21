using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The creation form: what it writes onto the instance before anything is downloaded.
/// </summary>
public sealed class LibraryCreateTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly LibraryViewModel _viewModel;

    public LibraryCreateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-create-" + Guid.NewGuid().ToString("N"));
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

    private static VersionManifestEntry Release(string id) => new()
    {
        Id = id,
        Type = "release",
        Url = $"https://example.invalid/{id}.json",
        Sha1 = new string('0', 40),
        ReleaseTime = DateTimeOffset.UtcNow,
    };

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_form_persists_the_loader_and_its_version_on_the_instance()
    {
        _viewModel.NewName = " Fabric pack ";
        _viewModel.NewVersion = Release("1.21.1");
        _viewModel.NewLoader = LoaderKind.Fabric;
        _viewModel.NewLoaderVersion = new LoaderVersionInfo(
            LoaderKind.Fabric,
            "0.19.5",
            "1.21.1",
            true,
            null);

        var record = await _viewModel.CreateInstanceRecordAsync();

        Assert.Equal("Fabric pack", record.Name);
        Assert.Equal("1.21.1", record.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, record.Loader);
        Assert.Equal("0.19.5", record.LoaderVersion);

        // The loader version is what determines the version the instance launches.
        Assert.Equal("fabric-loader-0.19.5-1.21.1", InstanceLauncher.LaunchVersionId(record));

        // And it survives a reload of the instance store, so the library list agrees.
        var reloaded = await new InstanceStore(_services.Paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<InstanceStore>.Instance)
            .LoadAllAsync(TestContext.Current.CancellationToken);
        var stored = Assert.Single(reloaded, candidate => candidate.Id == record.Id);
        Assert.Equal(LoaderKind.Fabric, stored.Loader);
        Assert.Equal("0.19.5", stored.LoaderVersion);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_vanilla_instance_has_no_loader_version()
    {
        _viewModel.NewName = "Vanilla";
        _viewModel.NewVersion = Release("26.3");
        _viewModel.NewLoader = LoaderKind.Vanilla;
        _viewModel.NewLoaderVersion = null;

        var record = await _viewModel.CreateInstanceRecordAsync();

        Assert.Equal(LoaderKind.Vanilla, record.Loader);
        Assert.Null(record.LoaderVersion);
        Assert.Equal("26.3", InstanceLauncher.LaunchVersionId(record));
    }
}
