using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.Core.Content;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>
/// One item in an instance's content directories: a resource pack, a shader pack, or a datapack. The
/// mechanics are the same as for mods - disabling renames, removal keeps the item in backups - so a
/// pack a user installed by hand is never destroyed by a click.
/// </summary>
public sealed partial class InstanceContentItemViewModel : ObservableObject
{
    private readonly string _backupDirectory;
    private readonly Action _onChanged;

    public InstanceContentItemViewModel(ContentFileEntry entry, string backupDirectory, Action onChanged)
    {
        Entry = entry;
        _backupDirectory = backupDirectory;
        _onChanged = onChanged;
    }

    public ContentFileEntry Entry { get; }

    public string FileName => Entry.FileName;

    public string SizeText => ByteSize.Format(Entry.Size);

    public bool IsEnabled => Entry.Enabled;

    /// <summary>True when the pack declares formats that exclude this instance's.</summary>
    public bool IsPackMismatch => Entry.IsPackMismatch;

    public string? PackFormatText => Entry.PackFormatText;

    public bool HasPackFormat => !string.IsNullOrEmpty(Entry.PackFormatText);

    public string? CompatibilityText => Entry.CompatibilityText;

    public bool HasCompatibility => !string.IsNullOrEmpty(Entry.CompatibilityText);

    [ObservableProperty]
    private string? _note;

    [RelayCommand]
    private void Toggle()
    {
        if (TrySetEnabled(!Entry.Enabled))
        {
            _onChanged();
        }
    }

    [RelayCommand]
    private void Remove()
    {
        if (TryRemove())
        {
            _onChanged();
        }
    }

    [RelayCommand]
    private void OpenFolder() =>
        ShellOpen.Directory(Path.GetDirectoryName(Entry.FilePath) ?? Entry.FilePath);

    /// <summary>Renames the file or folder. Returns false and records why when the rename fails.</summary>
    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            InstanceContentManager.SetEnabled(Entry.FilePath, enabled);
            Note = null;
            return true;
        }
        catch (Exception exception)
        {
            Note = exception.Message;
            return false;
        }
    }

    /// <summary>Moves the item into the launcher's backups rather than deleting it.</summary>
    public bool TryRemove()
    {
        try
        {
            InstanceContentManager.RemoveToBackup(Entry.FilePath, _backupDirectory);
            Note = null;
            return true;
        }
        catch (Exception exception)
        {
            Note = exception.Message;
            return false;
        }
    }
}

/// <summary>The datapacks belonging to one saved world.</summary>
public sealed class WorldDatapacksViewModel
{
    public WorldDatapacksViewModel(
        string worldName,
        string directoryPath,
        IReadOnlyList<ContentFileEntry> datapacks,
        string backupDirectory,
        Action onChanged)
    {
        WorldName = worldName;
        DirectoryPath = directoryPath;
        Datapacks = new ObservableCollection<InstanceContentItemViewModel>(
            datapacks.Select(entry => new InstanceContentItemViewModel(entry, backupDirectory, onChanged)));
    }

    public string WorldName { get; }

    public string DirectoryPath { get; }

    public ObservableCollection<InstanceContentItemViewModel> Datapacks { get; }

    public bool HasDatapacks => Datapacks.Count > 0;
}

/// <summary>
/// One screenshot, decoded small enough to show a gallery of them without holding full-resolution
/// images in memory.
/// </summary>
public sealed partial class ScreenshotItemViewModel : ObservableObject
{
    private const int ThumbnailWidth = 320;

    public ScreenshotItemViewModel(ContentFileEntry entry)
    {
        Entry = entry;
    }

    public ContentFileEntry Entry { get; }

    public string FileName => Entry.FileName;

    public string DetailText =>
        $"{ByteSize.Format(Entry.Size)} · {Entry.ModifiedAt.LocalDateTime:yyyy-MM-dd HH:mm}";

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    [ObservableProperty]
    private string? _note;

    /// <summary>Decodes a bounded thumbnail. A file that is not a readable image says so.</summary>
    public void LoadThumbnail()
    {
        try
        {
            using var stream = File.OpenRead(Entry.FilePath);
            Thumbnail = Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            Note = null;
        }
        catch (Exception exception)
        {
            Thumbnail = null;
            Note = exception.Message;
        }

        OnPropertyChanged(nameof(HasThumbnail));
    }

    [RelayCommand]
    private void OpenFolder() =>
        ShellOpen.File(Entry.FilePath);
}
