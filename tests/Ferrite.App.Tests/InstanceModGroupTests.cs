using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The mod tab's group filter and editor. A group is a rule over the installed mods, so the tests
/// assert that the filter narrows the visible list and that a group survives a reload.
/// </summary>
public sealed class InstanceModGroupTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceModGroupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-modgroups-ui-" + Guid.NewGuid().ToString("N"));
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
    public async Task The_mod_list_can_be_filtered_by_a_group()
    {
        var (viewModel, modsDirectory, record) = await CreateAsync();
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "alpha-storage.jar"), "alpha", "Alpha Storage", "1.0.0", 64, "fabric-api");
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "beta-maps.jar"), "beta", "Beta Maps", "2.0.3", 64, "fabric-api");
        ModArchiveFixture.WriteForge(Path.Combine(modsDirectory, "gamma.jar"), "gamma", "Gamma Core", "3.1.0", 64);
        await viewModel.RefreshModsAsync();

        // Only "all groups" until one is defined.
        Assert.Single(viewModel.ModGroupChoices);
        Assert.Equal(InstanceDetailViewModel.AllModGroups, viewModel.ModGroupChoices[0].Value);

        ModGroup.Upsert(record.ModGroups, "Storage", "storage");
        await _services.Instances.SaveAsync(record, CancellationToken.None);
        await viewModel.RefreshModsAsync();

        Assert.Equal(
            new[] { InstanceDetailViewModel.AllModGroups, "Storage" },
            viewModel.ModGroupChoices.Select(choice => choice.Value).ToList());

        viewModel.SelectedModGroup = viewModel.ModGroupChoices.Single(choice => choice.Value == "Storage");
        Assert.Equal("Alpha Storage", Assert.Single(viewModel.Mods).DisplayName);

        // The group composes with the search box rather than replacing it.
        viewModel.ModQuery = "maps";
        Assert.Empty(viewModel.Mods);
        Assert.True(viewModel.HasNoModMatches);

        viewModel.ModQuery = string.Empty;
        viewModel.SelectedModGroup = viewModel.ModGroupChoices[0];
        Assert.Equal(3, viewModel.Mods.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_editor_adds_and_removes_a_group_and_persists_it()
    {
        var (viewModel, modsDirectory, record) = await CreateAsync();
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "alpha-storage.jar"), "alpha", "Alpha Storage", "1.0.0", 64, "fabric-api");
        await viewModel.RefreshModsAsync();

        Assert.False(viewModel.IsManagingModGroups);
        viewModel.OpenModGroupsCommand.Execute(null);
        Assert.True(viewModel.IsManagingModGroups);

        // A group with no name or no rule is refused rather than silently matching everything.
        viewModel.NewModGroupName = "Storage";
        viewModel.NewModGroupMatch = "   ";
        await viewModel.AddModGroupCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasModGroupError);
        Assert.Empty(record.ModGroups);

        viewModel.NewModGroupMatch = "storage";
        await viewModel.AddModGroupCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasModGroupError);
        Assert.Equal("Storage", Assert.Single(record.ModGroups).Name);

        // Saved to disk, so a reload of the instance still has it.
        var reloaded = await _services.Instances.LoadAsync(record.Id, CancellationToken.None);
        Assert.Equal("storage", Assert.Single(reloaded.ModGroups).Match);
        Assert.Contains(viewModel.ModGroupChoices, choice => choice.Value == "Storage");

        // Removing it takes the group, and its filter entry, away.
        viewModel.SelectedManagedGroup = viewModel.ModGroupChoices.Single(choice => choice.Value == "Storage");
        Assert.True(viewModel.CanRemoveManagedGroup);
        await viewModel.RemoveModGroupCommand.ExecuteAsync(null);
        Assert.Empty(record.ModGroups);
        Assert.Single(viewModel.ModGroupChoices);
        Assert.False(viewModel.CanRemoveManagedGroup);

        viewModel.CloseModGroupsCommand.Execute(null);
        Assert.False(viewModel.IsManagingModGroups);
    }

    private async Task<(InstanceDetailViewModel ViewModel, string ModsDirectory, InstanceRecord Record)>
        CreateAsync()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Modded",
                MinecraftVersion = "1.21.1",
                Loader = Ferrite.Core.Minecraft.LoaderKind.Fabric,
            },
            CancellationToken.None);
        var modsDirectory = Path.Combine(_services.Paths.InstanceGameDirectory(record.Id), "mods");
        Directory.CreateDirectory(modsDirectory);
        var shell = new MainWindowViewModel(_services);
        return (new InstanceDetailViewModel(record, _services, shell), modsDirectory, record);
    }
}
