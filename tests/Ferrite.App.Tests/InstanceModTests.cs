using System.Text;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.App.Tests;

/// <summary>
/// The mod tab against real files: four loaders' worth of metadata read out of real archives, a
/// search that has to narrow the list, and a disable/enable/remove cycle that has to be reversible
/// on disk rather than only in the list.
/// </summary>
public sealed class InstanceModTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public InstanceModTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-mods-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>An instance on a real data root, plus the folders the mod tab reads.</summary>
    private async Task<(InstanceDetailViewModel ViewModel, string ModsDirectory)> CreateInstanceAsync()
    {
        var record = await _services.Instances.CreateAsync(
            new InstanceRecord { Id = Guid.NewGuid(), Name = "Modded", MinecraftVersion = "1.21.1" },
            CancellationToken.None);
        var modsDirectory = Path.Combine(_services.Paths.InstanceGameDirectory(record.Id), "mods");
        Directory.CreateDirectory(modsDirectory);
        var shell = new MainWindowViewModel(_services);
        return (new InstanceDetailViewModel(record, _services, shell), modsDirectory);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_mod_list_is_searched_filtered_and_sorted()
    {
        var (viewModel, modsDirectory) = await CreateInstanceAsync();
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "alpha.jar"), "alpha", "Alpha Storage", "1.0.0", 64, "fabric-api");
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "beta.jar"), "beta", "Beta Maps", "2.0.3", 256, "cloth-config");
        ModArchiveFixture.WriteForge(
            Path.Combine(modsDirectory, "gamma.jar"), "gamma", "Gamma Core", "3.1.0", 128);
        ModArchiveFixture.WritePlain(Path.Combine(modsDirectory, "delta.zip"), 32);

        await viewModel.RefreshModsAsync();

        Assert.Equal(4, viewModel.Mods.Count);
        Assert.True(viewModel.HasAnyMods);
        Assert.False(viewModel.HasNoModMatches);
        Assert.True(viewModel.HasModFilterNote);

        // The loader filter offers the loaders this instance actually has, plus "any".
        var loaders = viewModel.ModLoaderChoices.Select(choice => choice.Value).ToList();
        Assert.Equal(InstanceDetailViewModel.AllLoaders, loaders[0]);
        Assert.Contains("fabric", loaders);
        Assert.Contains("forge", loaders);
        Assert.Contains("unknown", loaders);

        // A query matches a name.
        viewModel.ModQuery = "maps";
        Assert.Equal("Beta Maps", Assert.Single(viewModel.Mods).DisplayName);

        // A query matches a loader, which is what makes "what is this file for" answerable.
        viewModel.ModQuery = "fabric";
        Assert.Equal(2, viewModel.Mods.Count);

        // A query matches a dependency the file declares.
        viewModel.ModQuery = "fabric-api";
        Assert.Equal("Alpha Storage", Assert.Single(viewModel.Mods).DisplayName);

        // A query that matches nothing reports it rather than looking like an empty folder.
        viewModel.ModQuery = "no-such-mod";
        Assert.Empty(viewModel.Mods);
        Assert.True(viewModel.HasNoModMatches);
        Assert.True(viewModel.HasAnyMods);

        // The loader filter narrows the list on its own.
        viewModel.ModQuery = string.Empty;
        viewModel.SelectedModLoader = viewModel.ModLoaderChoices.First(choice => choice.Value == "forge");
        Assert.Equal("Gamma Core", Assert.Single(viewModel.Mods).DisplayName);

        viewModel.SelectedModLoader = viewModel.ModLoaderChoices[0];
        Assert.Equal(4, viewModel.Mods.Count);

        // Size ordering puts the largest first, and it is a stable answer, not the scan order.
        viewModel.SelectedModSort = viewModel.ModSortChoices.First(choice => choice.Value == "size");
        Assert.Equal("Beta Maps", viewModel.Mods[0].DisplayName);

        viewModel.SelectedModSort = viewModel.ModSortChoices.First(choice => choice.Value == "name");
        Assert.Equal(
            new[] { "Alpha Storage", "Beta Maps", "delta", "Gamma Core" },
            viewModel.Mods.Select(mod => mod.DisplayName).ToList());

        // Sorting inside a filter, not only over the whole list.
        viewModel.ModQuery = "a";
        viewModel.SelectedModSort = viewModel.ModSortChoices.First(choice => choice.Value == "size");
        Assert.Equal(4, viewModel.Mods.Count);
        Assert.Equal(
            viewModel.Mods.Select(mod => mod.Metadata.Size).OrderByDescending(size => size).ToList(),
            viewModel.Mods.Select(mod => mod.Metadata.Size).ToList());
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_mod_can_be_disabled_re_enabled_and_removed()
    {
        var (viewModel, modsDirectory) = await CreateInstanceAsync();
        var incoming = Path.Combine(_root, "incoming");
        Directory.CreateDirectory(incoming);
        var dropped = Path.Combine(incoming, "sodium.jar");
        ModArchiveFixture.WriteFabric(dropped, "sodium", "Sodium", "0.6.0", 96, "fabric-api");
        var second = Path.Combine(incoming, "lithium.jar");
        ModArchiveFixture.WriteFabric(second, "lithium", "Lithium", "0.14.0", 48, "fabric-api");
        // A file the picker should refuse: not a mod archive at all.
        File.WriteAllText(Path.Combine(incoming, "notes.txt"), "not a mod", Encoding.UTF8);

        await viewModel.InstallModFilesAsync([dropped, second, Path.Combine(incoming, "notes.txt")]);

        Assert.True(File.Exists(Path.Combine(modsDirectory, "sodium.jar")));
        Assert.True(File.Exists(Path.Combine(modsDirectory, "lithium.jar")));
        Assert.False(File.Exists(Path.Combine(modsDirectory, "notes.txt")));
        Assert.Equal(2, viewModel.Mods.Count);

        var sodium = viewModel.Mods.Single(mod => mod.FileName == "sodium.jar");
        Assert.True(sodium.IsEnabled);

        sodium.ToggleCommand.Execute(null);
        await viewModel.RefreshModsAsync();

        // Disabling renames the file rather than deleting it, so the mod is still there to re-enable.
        Assert.False(File.Exists(Path.Combine(modsDirectory, "sodium.jar")));
        Assert.True(File.Exists(Path.Combine(modsDirectory, "sodium.jar.disabled")));
        var disabled = viewModel.Mods.Single(mod => mod.FileName == "sodium.jar.disabled");
        Assert.False(disabled.IsEnabled);
        Assert.Equal("sodium", disabled.Metadata.ModId);

        disabled.ToggleCommand.Execute(null);
        await viewModel.RefreshModsAsync();

        Assert.True(File.Exists(Path.Combine(modsDirectory, "sodium.jar")));
        Assert.True(viewModel.Mods.Single(mod => mod.FileName == "sodium.jar").IsEnabled);

        viewModel.Mods.Single(mod => mod.FileName == "sodium.jar").RemoveCommand.Execute(null);
        await viewModel.RefreshModsAsync();

        Assert.False(File.Exists(Path.Combine(modsDirectory, "sodium.jar")));
        Assert.False(File.Exists(Path.Combine(modsDirectory, "sodium.jar.disabled")));
        Assert.Equal("lithium.jar", Assert.Single(viewModel.Mods).FileName);

        // Removal keeps the file: it moves to the launcher's backups folder rather than vanishing.
        var backup = Assert.Single(Directory.EnumerateFiles(viewModel.ContentBackupDirectory));
        Assert.EndsWith("sodium.jar", backup, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(dropped), File.ReadAllBytes(backup));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Bulk_actions_apply_to_the_selected_mods_only()
    {
        var (viewModel, modsDirectory) = await CreateInstanceAsync();
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "alpha.jar"), "alpha", "Alpha", "1.0.0", 32, "fabric-api");
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "beta.jar"), "beta", "Beta", "1.0.0", 32, "fabric-api");
        ModArchiveFixture.WriteFabric(
            Path.Combine(modsDirectory, "gamma.jar"), "gamma", "Gamma", "1.0.0", 32, "fabric-api");

        await viewModel.RefreshModsAsync();
        Assert.False(viewModel.HasModSelection);
        Assert.Null(viewModel.SelectedModSummary);

        viewModel.SelectAllModsCommand.Execute(null);
        Assert.True(viewModel.HasModSelection);
        Assert.Equal("3 selected", viewModel.SelectedModSummary);

        await viewModel.DisableSelectedModsCommand.ExecuteAsync(null);

        Assert.All(viewModel.Mods, mod => Assert.False(mod.IsEnabled));
        Assert.True(File.Exists(Path.Combine(modsDirectory, "alpha.jar.disabled")));
        Assert.True(File.Exists(Path.Combine(modsDirectory, "beta.jar.disabled")));
        // The scan rebuilds the list, so nothing stays ticked under a bulk action that just ran.
        Assert.False(viewModel.HasModSelection);

        viewModel.SelectAllModsCommand.Execute(null);
        await viewModel.EnableSelectedModsCommand.ExecuteAsync(null);
        Assert.All(viewModel.Mods, mod => Assert.True(mod.IsEnabled));

        // "Select all" takes what the filter shows, not the mods it hid.
        viewModel.ModQuery = "alpha";
        viewModel.SelectAllModsCommand.Execute(null);
        Assert.Equal(1, viewModel.Mods.Count(mod => mod.IsSelected));
        Assert.Equal("1 selected", viewModel.SelectedModSummary);

        await viewModel.RemoveSelectedModsCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(modsDirectory, "alpha.jar")));
        Assert.Equal(2, Directory.EnumerateFiles(modsDirectory).Count());
        // The query is still in place, so clearing it is what brings the remaining mods back.
        Assert.Empty(viewModel.Mods);
        viewModel.ModQuery = string.Empty;
        Assert.Equal(2, viewModel.Mods.Count);
        Assert.EndsWith(
            "alpha.jar",
            Assert.Single(Directory.EnumerateFiles(viewModel.ContentBackupDirectory)),
            StringComparison.Ordinal);
    }

}
