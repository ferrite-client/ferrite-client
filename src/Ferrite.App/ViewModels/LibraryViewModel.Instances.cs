using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;

namespace Ferrite.App.ViewModels;

/// <summary>Rename and clone prompts for the library.</summary>
public sealed partial class LibraryViewModel
{
    [ObservableProperty]
    private InstanceCardViewModel? _renameTarget;

    [ObservableProperty]
    private string _renameName = string.Empty;

    [ObservableProperty]
    private InstanceCardViewModel? _cloneTarget;

    [ObservableProperty]
    private string _cloneName = string.Empty;

    public bool IsRenaming => RenameTarget is not null;

    public bool IsCloning => CloneTarget is not null;

    partial void OnRenameTargetChanged(InstanceCardViewModel? value) => OnPropertyChanged(nameof(IsRenaming));

    partial void OnCloneTargetChanged(InstanceCardViewModel? value) => OnPropertyChanged(nameof(IsCloning));

    /// <summary>Opens the rename prompt for an instance.</summary>
    public void BeginRename(InstanceCardViewModel card)
    {
        RenameName = card.Name;
        RenameTarget = card;
    }

    /// <summary>Opens the clone prompt for an instance.</summary>
    public void BeginClone(InstanceCardViewModel card)
    {
        CloneName = card.Name + " (copy)";
        CloneTarget = card;
    }

    [RelayCommand]
    private void CancelRename()
    {
        RenameTarget = null;
        RenameName = string.Empty;
    }

    [RelayCommand]
    private void CancelClone()
    {
        CloneTarget = null;
        CloneName = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        if (RenameTarget is not { } target || string.IsNullOrWhiteSpace(RenameName))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _services.InstanceManager
                .RenameAsync(target.Record.Id, RenameName, CancellationToken.None)
                .ConfigureAwait(true);
            RenameTarget = null;
            _shell.ReportStatus(Localizer.Get("L.Library.InstanceRenamed"));
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            FormError = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmCloneAsync()
    {
        if (CloneTarget is not { } target)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Cloning {target.Name}...");
            var clone = await _services.InstanceManager
                .CloneAsync(target.Record.Id, CloneName, CancellationToken.None)
                .ConfigureAwait(true);
            CloneTarget = null;
            _shell.ReportStatus(Localizer.Format("L.Library.InstanceCreated", clone.Name));
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            FormError = exception.Message;
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }
}
