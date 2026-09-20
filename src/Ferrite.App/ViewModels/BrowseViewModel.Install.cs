using CommunityToolkit.Mvvm.Input;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>Install behaviour for the browser.</summary>
public sealed partial class BrowseViewModel
{
    [RelayCommand]
    private async Task InstallAsync()
    {
        if (TargetInstance is not { } instance || SelectedVersion is not { } version)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Installing {SelectedResult?.Title ?? version.VersionNumber}...");
            StatusNote = null;

            var plan = await _services.Content
                .PlanAsync(
                    instance,
                    version.ProjectId,
                    version.VersionId,
                    IncludeOptionalDependencies,
                    CancellationToken.None)
                .ConfigureAwait(true);

            var gameDirectory = _services.Paths.InstanceGameDirectory(instance.Id);
            var result = await _services.Content
                .InstallAsync(
                    instance,
                    plan,
                    gameDirectory,
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            StatusNote = $"Installed {result.Installed} file(s) into {instance.Name}";
            if (result.Warnings.Count > 0)
            {
                StatusNote += $" · {result.Warnings.Count} warning(s)";
            }

            _shell.ReportStatus(StatusNote);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }

    [RelayCommand]
    private void OpenProjectPage()
    {
        if (SelectedResult is { } project)
        {
            ShellOpen.Url($"https://modrinth.com/project/{project.Slug}");
        }
    }

    [RelayCommand]
    private async Task RefreshInstancesAsync() => await LoadInstancesAsync().ConfigureAwait(true);
}
