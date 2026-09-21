using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The local-server tab's own wiring: the page shows the settings the server would actually use, read
/// from the server's file rather than from a second copy the launcher keeps.
/// </summary>
public sealed class InstanceLocalServerViewTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceLocalServerViewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-server-ui-" + Guid.NewGuid().ToString("N"));
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

    private async Task<InstanceRecord> CreateAsync()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Host",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Vanilla,
                MemoryMb = 6144,
            },
            CancellationToken.None);
        Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));
        return record;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_page_shows_the_servers_own_settings()
    {
        var record = await CreateAsync();
        var serverDirectory = _services.LocalServers.ServerDirectory(record.Id);
        Directory.CreateDirectory(serverDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(serverDirectory, LocalServerService.PropertiesName),
            "# kept by the user" + Environment.NewLine
            + "difficulty=hard" + Environment.NewLine
            + "level-name=survival" + Environment.NewLine
            + "server-port=25580" + Environment.NewLine
            + "max-players=8" + Environment.NewLine
            + "online-mode=false" + Environment.NewLine
            + "motd=Hello" + Environment.NewLine,
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(serverDirectory, LocalServerService.EulaName),
            "eula=true" + Environment.NewLine,
            CancellationToken.None);

        var viewModel = new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services));

        Assert.Equal("survival", viewModel.ServerLevelName);
        Assert.Equal(25580, viewModel.ServerPort);
        Assert.Equal(8, viewModel.ServerMaxPlayers);
        Assert.False(viewModel.ServerOnlineMode);
        Assert.Equal("Hello", viewModel.ServerMotd);
        Assert.True(viewModel.ServerAcceptEula);
        // Memory comes from the instance, because that is where the launcher's choice lives.
        Assert.Equal(6144, viewModel.ServerMemoryMb);
        Assert.False(viewModel.IsServerRunning);
        Assert.True(viewModel.CanStartServer);
        Assert.False(viewModel.HasServerStatus);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_server_that_was_never_prepared_still_opens()
    {
        var record = await CreateAsync();

        var viewModel = new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services));

        Assert.Equal("world", viewModel.ServerLevelName);
        Assert.Equal(25565, viewModel.ServerPort);
        Assert.True(viewModel.ServerOnlineMode);
        Assert.False(viewModel.ServerAcceptEula);
        Assert.True(viewModel.CanStartServer);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Exporting_without_a_path_reports_nothing_and_writes_nothing()
    {
        var record = await CreateAsync();
        var viewModel = new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services));

        Assert.False(await viewModel.ExportServerAsync(null));
        Assert.False(viewModel.HasServerStatus);
    }
}
