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
/// Switching a loader instance to another loader version. The ordering is the whole point: the version
/// is installed first, and the instance is only pointed at it afterwards.
/// </summary>
public sealed class LoaderSwitchTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public LoaderSwitchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-loaderswitch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
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

    private async Task<InstanceDetailViewModel> LoaderInstanceAsync(
        string loaderVersion = "0.19.5")
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Loader instance",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Fabric,
                LoaderVersion = loaderVersion,
            },
            CancellationToken.None);
        return new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services));
    }

    private static LoaderVersionInfo Version(string version) =>
        new(LoaderKind.Fabric, version, "1.21.1", Stable: true, Maven: null);

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_successful_switch_installs_first_then_moves_the_instance()
    {
        var viewModel = await LoaderInstanceAsync();
        var order = new List<string>();

        var switched = await viewModel.ApplyLoaderVersionAsync(
            Version("0.19.3"),
            async version =>
            {
                order.Add("install");
                // At the moment of installation the instance still names the old version.
                order.Add($"record={viewModel.Record.LoaderVersion}");
                // The switcher moves the instance only once the install has succeeded.
                viewModel.Record.Loader = version.Kind;
                viewModel.Record.LoaderVersion = version.Version;
                await _services.Instances.SaveAsync(viewModel.Record, TestContext.Current.CancellationToken);
            });

        Assert.True(switched);
        Assert.Equal(["install", "record=0.19.5"], order);
        Assert.Equal("0.19.3", viewModel.Record.LoaderVersion);
        Assert.Equal(LoaderKind.Fabric, viewModel.Record.Loader);

        // The launch version follows the record, which is what the game is started with.
        Assert.Equal("fabric-loader-0.19.3-1.21.1", InstanceLauncher.LaunchVersionId(viewModel.Record));
        Assert.Equal("Fabric 0.19.3", viewModel.LoaderSummary);

        // And it survives a reload, so the library agrees with the detail page.
        var stored = (await _services.Instances.LoadAllAsync(TestContext.Current.CancellationToken))
            .Single(record => record.Id == viewModel.Record.Id);
        Assert.Equal("0.19.3", stored.LoaderVersion);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_failed_install_leaves_the_instance_on_the_version_it_had()
    {
        var viewModel = await LoaderInstanceAsync();

        var switched = await viewModel.ApplyLoaderVersionAsync(
            Version("9.9.9"),
            _ => throw new InvalidOperationException("the loader source refused"));

        Assert.False(switched);
        Assert.Equal("0.19.5", viewModel.Record.LoaderVersion);
        Assert.Contains("refused", viewModel.LoaderSwitchStatus, StringComparison.OrdinalIgnoreCase);

        var stored = (await _services.Instances.LoadAllAsync(TestContext.Current.CancellationToken))
            .Single(record => record.Id == viewModel.Record.Id);
        Assert.Equal("0.19.5", stored.LoaderVersion);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Choosing_the_version_it_already_runs_does_nothing()
    {
        var viewModel = await LoaderInstanceAsync();
        var installed = false;

        var switched = await viewModel.ApplyLoaderVersionAsync(
            Version("0.19.5"),
            _ =>
            {
                installed = true;
                return Task.CompletedTask;
            });

        Assert.False(switched);
        Assert.False(installed);
        Assert.Contains("0.19.5", viewModel.LoaderSwitchStatus, StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_vanilla_instance_has_no_loader_version_to_choose()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord { Id = Guid.NewGuid(), Name = "Vanilla", MinecraftVersion = "1.21.1" },
            CancellationToken.None);
        var viewModel = new InstanceDetailViewModel(
            record,
            _services,
            new MainWindowViewModel(_services));

        Assert.False(viewModel.CanChangeLoader);
        Assert.False(await viewModel.ApplyLoaderVersionAsync(Version("0.19.3"), _ => Task.CompletedTask));
        Assert.Null(viewModel.Record.LoaderVersion);
    }
}
