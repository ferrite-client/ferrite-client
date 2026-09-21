using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The library's folder filter. A folder is only offered while something is filed under it, so an
/// emptied folder disappears instead of leaving a selection that shows nothing.
/// </summary>
public sealed class LibraryGroupTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public LibraryGroupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-groups-ui-" + Guid.NewGuid().ToString("N"));
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

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_library_filters_by_folder()
    {
        await AddAsync("Survival one", "Survival");
        await AddAsync("Survival two", "Survival");
        await AddAsync("Tech one", "Tech");
        await AddAsync("Unfiled", null);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new LibraryViewModel(_services, shell);
        await viewModel.RefreshAsync();

        // "All folders" is always first, then the folders in use, ordered by name.
        Assert.Equal(
            new[] { string.Empty, "Survival", "Tech" },
            viewModel.GroupChoices.Select(choice => choice.Value).ToList());

        // The default selection shows everything.
        Assert.Equal(4, viewModel.VisibleInstances.Count());

        viewModel.SelectedGroup = viewModel.GroupChoices.Single(choice => choice.Value == "Survival");
        Assert.Equal(
            new[] { "Survival one", "Survival two" },
            viewModel.VisibleInstances.Select(card => card.Name).OrderBy(name => name).ToList());

        // The folder filter composes with the search box.
        viewModel.SearchText = "two";
        Assert.Equal("Survival two", Assert.Single(viewModel.VisibleInstances).Name);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Moving_an_instance_between_folders_updates_the_filter()
    {
        var record = await AddAsync("Mover", null);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new LibraryViewModel(_services, shell);
        await viewModel.RefreshAsync();
        Assert.Single(viewModel.GroupChoices);

        var card = Assert.Single(viewModel.Instances);
        viewModel.BeginGroup(card);
        viewModel.GroupName = "Tech";
        await viewModel.ConfirmGroupCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.GroupChoices, choice => choice.Value == "Tech");
        viewModel.SelectedGroup = viewModel.GroupChoices.Single(choice => choice.Value == "Tech");
        Assert.Equal("Mover", Assert.Single(viewModel.VisibleInstances).Name);

        // Clearing the folder takes the instance, and therefore the folder, out of the filter.
        viewModel.BeginGroup(Assert.Single(viewModel.Instances));
        viewModel.GroupName = string.Empty;
        await viewModel.ConfirmGroupCommand.ExecuteAsync(null);

        Assert.Single(viewModel.GroupChoices);
        Assert.DoesNotContain(viewModel.GroupChoices, choice => choice.Value == "Tech");
        var reloaded = Assert.Single(
            await _services.Instances.LoadAllAsync(CancellationToken.None),
            item => item.Id == record.Id);
        Assert.Equal("Mover", reloaded.Name);
        Assert.Null(reloaded.Group);
    }

    private async Task<InstanceRecord> AddAsync(string name, string? group)
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = name,
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Vanilla,
                Group = group,
            },
            CancellationToken.None);
        Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));
        return record;
    }
}
