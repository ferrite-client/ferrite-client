using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Storage;

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

    [ObservableProperty]
    private InstanceCardViewModel? _groupTarget;

    [ObservableProperty]
    private string _groupName = string.Empty;

    public bool IsRenaming => RenameTarget is not null;

    public bool IsCloning => CloneTarget is not null;

    public bool IsGrouping => GroupTarget is not null;

    partial void OnRenameTargetChanged(InstanceCardViewModel? value) => OnPropertyChanged(nameof(IsRenaming));

    partial void OnCloneTargetChanged(InstanceCardViewModel? value) => OnPropertyChanged(nameof(IsCloning));

    partial void OnGroupTargetChanged(InstanceCardViewModel? value) => OnPropertyChanged(nameof(IsGrouping));

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

    /// <summary>Opens the folder prompt for an instance, pre-filled with its current folder.</summary>
    public void BeginGroup(InstanceCardViewModel card)
    {
        GroupName = card.Record.Group ?? string.Empty;
        GroupTarget = card;
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
    private void CancelGroup()
    {
        GroupTarget = null;
        GroupName = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmGroupAsync()
    {
        if (GroupTarget is not { } target)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var group = InstanceManager.NormalizeGroup(GroupName);
            await _services.InstanceManager
                .SetGroupAsync(target.Record.Id, group, CancellationToken.None)
                .ConfigureAwait(true);
            GroupTarget = null;
            _shell.ReportStatus(group is null
                ? Localizer.Get("L.Library.GroupCleared")
                : Localizer.Format("L.Library.Grouped", target.Name, group));
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
