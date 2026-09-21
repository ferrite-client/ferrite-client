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
/// The library list against real instances: the search box, each order, and the per-instance size the
/// cards report.
/// </summary>
public sealed class LibraryListTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public LibraryListTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-library-" + Guid.NewGuid().ToString("N"));
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

    private async Task<LibraryViewModel> SeedAsync()
    {
        await AddAsync("Zebra pack", "1.20.4", LoaderKind.Forge, "47.2.0", bytes: 4096, playedDaysAgo: 9);
        await AddAsync("Alpha survival", "26.3", LoaderKind.Vanilla, null, bytes: 1024, playedDaysAgo: 1);
        await AddAsync("middle modded", "1.21.1", LoaderKind.Fabric, "0.19.5", bytes: 65536, playedDaysAgo: 4);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new LibraryViewModel(_services, shell);
        await viewModel.RefreshAsync();
        return viewModel;
    }

    private async Task AddAsync(
        string name,
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        int bytes,
        int playedDaysAgo)
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                MinecraftVersion = minecraftVersion,
                Loader = loader,
                LoaderVersion = loaderVersion,
                LastLaunchedAt = DateTimeOffset.UtcNow.AddDays(-playedDaysAgo),
            },
            CancellationToken.None);

        // A real file makes the instance's disk usage something other than zero.
        var gameDirectory = _services.Paths.InstanceGameDirectory(record.Id);
        Directory.CreateDirectory(Path.Combine(gameDirectory, "mods"));
        await File.WriteAllBytesAsync(
            Path.Combine(gameDirectory, "mods", "bulk.bin"),
            new byte[bytes],
            CancellationToken.None);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_library_can_be_searched_and_ordered()
    {
        var viewModel = await SeedAsync();

        Assert.Equal(3, viewModel.Instances.Count);

        // Recently played first is the default.
        Assert.Equal(
            new[] { "Alpha survival", "middle modded", "Zebra pack" },
            viewModel.VisibleInstances.Select(card => card.Name).ToList());

        viewModel.SelectedSort = viewModel.SortChoices.Single(choice => choice.Value == "name");
        Assert.Equal(
            new[] { "Alpha survival", "middle modded", "Zebra pack" },
            viewModel.VisibleInstances.Select(card => card.Name).ToList());

        viewModel.SelectedSort = viewModel.SortChoices.Single(choice => choice.Value == "version");
        Assert.Equal(
            new[] { "Zebra pack", "middle modded", "Alpha survival" },
            viewModel.VisibleInstances.Select(card => card.Name).ToList());

        viewModel.SelectedSort = viewModel.SortChoices.Single(choice => choice.Value == "size");
        Assert.Equal(
            new[] { "middle modded", "Zebra pack", "Alpha survival" },
            viewModel.VisibleInstances.Select(card => card.Name).ToList());

        // Every card reports a real size off the instance folder, which is what the size order uses.
        var middle = viewModel.Instances.Single(card => card.Name == "middle modded");
        Assert.True(middle.SizeBytes >= 65536, $"size was {middle.SizeBytes}");

        // The search box matches a name, a version, and a loader, which is what the card shows.
        viewModel.SearchText = "survival";
        Assert.Equal("Alpha survival", Assert.Single(viewModel.VisibleInstances).Name);

        viewModel.SearchText = "1.20.4";
        Assert.Equal("Zebra pack", Assert.Single(viewModel.VisibleInstances).Name);

        viewModel.SearchText = "fabric";
        Assert.Equal("middle modded", Assert.Single(viewModel.VisibleInstances).Name);

        viewModel.SearchText = "nothing matches this";
        Assert.Empty(viewModel.VisibleInstances);
    }
}
