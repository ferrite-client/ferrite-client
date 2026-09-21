using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Services;
using Ferrite.Core.Content;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>One installed mod file with its actions.</summary>
public sealed partial class ModItemViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _onChanged;
    private readonly Action? _onSelectionChanged;
    private readonly string _backupDirectory;

    public ModItemViewModel(
        ModMetadata metadata,
        AppServices services,
        Action onChanged,
        string backupDirectory,
        Action? onSelectionChanged = null)
    {
        Metadata = metadata;
        _services = services;
        _onChanged = onChanged;
        _backupDirectory = backupDirectory;
        _onSelectionChanged = onSelectionChanged;
    }

    public ModMetadata Metadata { get; }

    public string DisplayName => Metadata.DisplayName;

    public string FileName => Metadata.FileName;

    public string Loader => Metadata.Loader;

    public string VersionText => Metadata.Version ?? "unknown version";

    public string SizeText => ByteSize.Format(Metadata.Size);

    public string? Description => Metadata.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Metadata.Description);

    public string DependencyText => Metadata.Dependencies.Count == 0
        ? string.Empty
        : "requires " + string.Join(", ", Metadata.Dependencies);

    public bool HasDependencies => Metadata.Dependencies.Count > 0;

    public bool IsEnabled => Metadata.Enabled;

    /// <summary>Marks this mod for a bulk action. Selection is a property of the list, not the file.</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke();

    [ObservableProperty]
    private string? _note;

    [RelayCommand]
    private void Toggle()
    {
        if (TrySetEnabled(!Metadata.Enabled))
        {
            _onChanged();
        }
    }

    /// <summary>Enables or disables the file. Returns false and records why when the rename fails.</summary>
    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            InstanceContentManager.SetEnabled(Metadata.FilePath, enabled);
            Note = null;
            return true;
        }
        catch (Exception exception)
        {
            Note = exception.Message;
            return false;
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

    /// <summary>
    /// Takes the file out of the instance and keeps it in the launcher's backup folder, so removing
    /// a mod by mistake is recoverable. Returns false and records why when the move fails.
    /// </summary>
    public bool TryRemove()
    {
        try
        {
            InstanceContentManager.RemoveToBackup(Metadata.FilePath, _backupDirectory);
            Note = null;
            return true;
        }
        catch (Exception exception)
        {
            Note = exception.Message;
            return false;
        }
    }

    [RelayCommand]
    private void OpenFolder() =>
        ShellOpen.Directory(Path.GetDirectoryName(Metadata.FilePath) ?? _services.Paths.Root);
}
