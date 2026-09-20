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

    public ModItemViewModel(ModMetadata metadata, AppServices services, Action onChanged)
    {
        Metadata = metadata;
        _services = services;
        _onChanged = onChanged;
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

    [ObservableProperty]
    private string? _note;

    [RelayCommand]
    private void Toggle()
    {
        try
        {
            InstanceContentManager.SetEnabled(Metadata.FilePath, !Metadata.Enabled);
            _onChanged();
        }
        catch (Exception exception)
        {
            Note = exception.Message;
        }
    }

    [RelayCommand]
    private void Delete()
    {
        try
        {
            InstanceContentManager.Delete(Metadata.FilePath);
            _onChanged();
        }
        catch (Exception exception)
        {
            Note = exception.Message;
        }
    }

    [RelayCommand]
    private void OpenFolder() =>
        ShellOpen.Directory(Path.GetDirectoryName(Metadata.FilePath) ?? _services.Paths.Root);
}
