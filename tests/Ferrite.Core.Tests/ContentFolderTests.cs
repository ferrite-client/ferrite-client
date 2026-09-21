using Ferrite.Core.Content;

namespace Ferrite.Core.Tests;

/// <summary>
/// Content that ships as a folder rather than a zip: a resource pack a user extracted by hand is a
/// directory, and disabling or removing it has to work the same way it does for a file.
/// </summary>
public sealed class ContentFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _packs;
    private readonly string _backups;

    public ContentFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-folders-" + Guid.NewGuid().ToString("N"));
        _packs = Path.Combine(_root, "resourcepacks");
        _backups = Path.Combine(_root, "backups");
        Directory.CreateDirectory(_packs);
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

    private string MakePack(string name)
    {
        var directory = Path.Combine(_packs, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "pack.mcmeta"), """{ "pack": { "pack_format": 34 } }""");
        return directory;
    }

    [Fact]
    public void A_folder_pack_is_disabled_and_re_enabled_by_renaming()
    {
        var pack = MakePack("folder-pack");

        var disabled = InstanceContentManager.SetEnabled(pack, enabled: false);

        Assert.Equal(Path.Combine(_packs, "folder-pack.disabled"), disabled);
        Assert.False(Directory.Exists(pack));
        Assert.True(File.Exists(Path.Combine(disabled, "pack.mcmeta")));

        var enabled = InstanceContentManager.SetEnabled(disabled, enabled: true);
        Assert.Equal(pack, enabled);
        Assert.True(Directory.Exists(pack));
    }

    [Fact]
    public void Removing_a_folder_pack_moves_the_whole_folder_to_backups()
    {
        var pack = MakePack("keeper");

        var moved = InstanceContentManager.RemoveToBackup(pack, _backups);

        Assert.NotNull(moved);
        Assert.False(Directory.Exists(pack));
        Assert.True(Directory.Exists(moved));
        Assert.True(File.Exists(Path.Combine(moved!, "pack.mcmeta")));
        Assert.EndsWith("keeper", moved, StringComparison.Ordinal);
    }

    [Fact]
    public void Listing_includes_folders_and_files_and_reports_their_size()
    {
        var folder = MakePack("folder-pack");
        var zip = Path.Combine(_packs, "zipped.zip");
        File.WriteAllBytes(zip, new byte[512]);

        var entries = InstanceContentManager.ListFolder(_root, "resourcepacks");

        Assert.Equal(2, entries.Count);
        var folderEntry = Assert.Single(entries, entry => entry.FileName == "folder-pack");
        Assert.True(folderEntry.Size > 0, "a folder pack should report the size of its contents");
        var zipEntry = Assert.Single(entries, entry => entry.FileName == "zipped.zip");
        Assert.Equal(512, zipEntry.Size);
        Assert.True(folderEntry.Enabled && zipEntry.Enabled);
    }

    [Fact]
    public void A_disabled_folder_pack_is_listed_as_disabled()
    {
        var pack = MakePack("off");
        InstanceContentManager.SetEnabled(pack, enabled: false);

        var entry = Assert.Single(InstanceContentManager.ListFolder(_root, "resourcepacks"));

        Assert.Equal("off.disabled", entry.FileName);
        Assert.False(entry.Enabled);
    }
}
