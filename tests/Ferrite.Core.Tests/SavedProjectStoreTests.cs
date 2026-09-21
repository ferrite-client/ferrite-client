using Ferrite.Core.Content;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Saved provider projects. The store is the only writer of its file, so the tests cover the file's
/// round trip, deduplication, and an unreadable document.
/// </summary>
public sealed class SavedProjectStoreTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;

    public SavedProjectStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-saved-" + Guid.NewGuid().ToString("N"));
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SavedProjectStore CreateStore() =>
        new(_paths, NullLogger<SavedProjectStore>.Instance);

    private static ContentSummary Project(string id, string title, string provider = "modrinth") => new(
        provider, id, id, title, "A description", ContentProjectType.Mod, 1000, null, "An author", [], null);

    [Fact]
    public async Task Toggling_saves_and_unsaves_a_project()
    {
        var store = CreateStore();
        var project = Project("sodium", "Sodium");

        Assert.False(await store.ContainsAsync("modrinth", "sodium", TestContext.Current.CancellationToken));
        Assert.True(await store.ToggleAsync(project, TestContext.Current.CancellationToken));
        Assert.True(await store.ContainsAsync("modrinth", "sodium", TestContext.Current.CancellationToken));

        var saved = Assert.Single(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Sodium", saved.Title);
        Assert.Equal(ContentProjectType.Mod, saved.ProjectType);

        Assert.False(await store.ToggleAsync(project, TestContext.Current.CancellationToken));
        Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.False(await store.ContainsAsync("modrinth", "sodium", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_entry_is_unique_per_provider_and_project()
    {
        var store = CreateStore();
        await store.ToggleAsync(Project("sodium", "Sodium"), TestContext.Current.CancellationToken);
        await store.ToggleAsync(Project("sodium", "Sodium"), TestContext.Current.CancellationToken);
        await store.ToggleAsync(Project("sodium", "Sodium"), TestContext.Current.CancellationToken);
        await store.ToggleAsync(Project("mekanism", "Mekanism"), TestContext.Current.CancellationToken);

        var saved = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, saved.Count);
        Assert.Equal("Mekanism", saved[0].Title);
    }

    [Fact]
    public async Task The_same_project_id_from_two_providers_is_two_entries()
    {
        var store = CreateStore();
        await store.ToggleAsync(Project("jei", "JEI", "modrinth"), TestContext.Current.CancellationToken);
        await store.ToggleAsync(Project("jei", "JEI", "curseforge"), TestContext.Current.CancellationToken);

        Assert.Equal(2, (await store.LoadAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Saved_projects_survive_a_new_store_over_the_same_root()
    {
        await CreateStore().ToggleAsync(Project("sodium", "Sodium"), TestContext.Current.CancellationToken);

        var reopened = await CreateStore().LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("sodium", Assert.Single(reopened).ProjectId);
    }

    [Fact]
    public async Task An_unreadable_document_starts_empty_instead_of_throwing()
    {
        await File.WriteAllTextAsync(
            _paths.SavedProjectsFile,
            "{ this is not json",
            TestContext.Current.CancellationToken);

        var store = CreateStore();
        Assert.Empty(await store.LoadAsync(TestContext.Current.CancellationToken));

        // The store stays usable: a save after the bad read persists the document it now owns.
        Assert.True(await store.ToggleAsync(Project("sodium", "Sodium"), TestContext.Current.CancellationToken));
        Assert.Single(await CreateStore().LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Removing_an_entry_that_is_not_saved_reports_that()
    {
        var store = CreateStore();
        Assert.False(await store.RemoveAsync("modrinth", "absent", TestContext.Current.CancellationToken));
    }
}
