using Ferrite.Core.Minecraft;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Mod groups are a rule, not a stored membership list, so a pack update cannot leave a group
/// pointing at mods that no longer exist.
/// </summary>
public sealed class ModGroupTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly InstanceStore _store;

    public ModGroupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-modgroups-" + Guid.NewGuid().ToString("N"));
        _paths = AppPaths.ForRoot(_root);
        _paths.EnsureCreated();
        _store = new InstanceStore(_paths, NullLogger<InstanceStore>.Instance);
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

    [Fact]
    public void A_group_matches_a_display_name_or_a_file_name()
    {
        var group = new ModGroup { Name = "Storage", Match = "storage" };

        Assert.True(group.Matches("Alpha Storage", null));
        Assert.True(group.Matches(null, "alpha-storage-1.0.jar"));
        Assert.True(group.Matches("Alpha Storage", "alpha.jar"));
        Assert.False(group.Matches("Beta Maps", "beta.jar"));
        Assert.False(group.Matches(null, null));
    }

    [Fact]
    public void An_empty_rule_matches_nothing()
    {
        var group = new ModGroup { Name = "Broken", Match = string.Empty };
        Assert.False(group.Matches("Anything", "anything.jar"));
    }

    [Fact]
    public void Upsert_adds_a_group_and_replaces_the_rule_of_one_with_the_same_name()
    {
        var groups = new List<ModGroup>();
        Assert.True(ModGroup.Upsert(groups, "  Storage  ", " storage "));
        Assert.Equal("Storage", Assert.Single(groups).Name);
        Assert.Equal("storage", groups[0].Match);

        Assert.True(ModGroup.Upsert(groups, "storage", "backpack"));
        var updated = Assert.Single(groups);
        Assert.Equal("storage", updated.Name);
        Assert.Equal("backpack", updated.Match);
    }

    [Fact]
    public void Upsert_refuses_a_blank_name_or_rule()
    {
        var groups = new List<ModGroup>();
        Assert.False(ModGroup.Upsert(groups, string.Empty, "storage"));
        Assert.False(ModGroup.Upsert(groups, "Storage", "   "));
        Assert.Empty(groups);
    }

    [Fact]
    public void Remove_takes_a_group_by_name_ignoring_case()
    {
        var groups = new List<ModGroup>();
        ModGroup.Upsert(groups, "Storage", "storage");
        ModGroup.Upsert(groups, "Tech", "industrial");

        Assert.True(ModGroup.Remove(groups, "storage"));
        Assert.False(ModGroup.Remove(groups, "storage"));
        Assert.Equal("Tech", Assert.Single(groups).Name);
    }

    [Fact]
    public async Task Groups_round_trip_through_the_instance_record()
    {
        var record = await _store.CreateAsync(
            new InstanceRecord
            {
                Id = Guid.NewGuid(),
                Name = "Grouped",
                MinecraftVersion = "1.21.1",
                Loader = LoaderKind.Fabric,
            },
            TestContext.Current.CancellationToken);

        ModGroup.Upsert(record.ModGroups, "Storage", "storage");
        await _store.SaveAsync(record, TestContext.Current.CancellationToken);

        var reloaded = await _store.LoadAsync(record.Id, TestContext.Current.CancellationToken);
        var group = Assert.Single(reloaded.ModGroups);
        Assert.Equal("Storage", group.Name);
        Assert.Equal("storage", group.Match);
    }
}
