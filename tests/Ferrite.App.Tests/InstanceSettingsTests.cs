using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.App.Tests;

/// <summary>
/// Settings that only count when they change something on disk or in a running service.
/// </summary>
public sealed class InstanceSettingsTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-instsettings-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// The EULA toggle has to produce the file the game reads. Saving used to store a flag that
    /// nothing acted on.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Saving_an_instance_writes_the_eula_file_it_promised()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord { Id = Guid.NewGuid(), Name = "Eula instance", MinecraftVersion = "1.21.1" },
            CancellationToken.None);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new InstanceDetailViewModel(record, _services, shell)
        {
            AcceptEula = true,
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        var gameDirectory = _services.Paths.InstanceGameDirectory(record.Id);
        Assert.True(File.Exists(EulaFile.Path(gameDirectory)), "eula.txt should exist after saving");
        Assert.True(EulaFile.IsAccepted(gameDirectory));

        // Turning it off again withdraws the acceptance the launcher wrote.
        viewModel.AcceptEula = false;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.False(EulaFile.IsAccepted(gameDirectory));
    }

    [Fact]
    public void Mirror_lines_round_trip_through_the_settings_view()
    {
        var text = """
            # a comment
            launchermeta.mojang.com=https://mirror.example/mojang

            piston-data.mojang.com = http://127.0.0.1:8080/mirror
            not-a-url
            broken=not a url
            """;

        var parsed = SettingsViewModel.ParseMirrors(text);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("https://mirror.example/mojang", parsed["launchermeta.mojang.com"]);
        Assert.Equal("http://127.0.0.1:8080/mirror", parsed["piston-data.mojang.com"]);

        // The formatted text re-parses to the same map, so saving twice is stable.
        var reformatted = SettingsViewModel.FormatMirrors(parsed);
        Assert.Equal(parsed, SettingsViewModel.ParseMirrors(reformatted));
    }

    [Fact]
    public async Task Saving_settings_pushes_the_network_values_into_the_services()
    {
        var shell = new MainWindowViewModel(_services);
        var viewModel = new SettingsViewModel(_services, shell)
        {
            MaxConcurrentDownloads = 3,
            ProxyUrl = "http://127.0.0.1:9",
            MirrorOverridesText = "launchermeta.mojang.com=https://mirror.example",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(3, _services.Downloads.MaxConcurrency);
        Assert.False(_services.Mirrors.IsEmpty);
        Assert.Equal(
            "https://mirror.example/v1/packages/x.json",
            _services.Mirrors.Resolve("https://launchermeta.mojang.com/v1/packages/x.json"));

        // The values survive a reload of the settings document.
        var reloaded = new SettingsStore(_services.Paths, NullLogger<SettingsStore>.Instance);
        var settings = await reloaded.LoadAsync(CancellationToken.None);
        Assert.Equal(3, settings.MaxConcurrentDownloads);
        Assert.Equal("http://127.0.0.1:9", settings.ProxyUrl);
        Assert.Equal("https://mirror.example", settings.MirrorOverrides["launchermeta.mojang.com"]);
    }
}
