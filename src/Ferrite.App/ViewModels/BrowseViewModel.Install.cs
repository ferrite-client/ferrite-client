using CommunityToolkit.Mvvm.Input;
using Ferrite.Core.Content;
using Ferrite.Core.Download;
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
            // A modpack defines its own game version, loader, and files, so it always becomes a new
            // instance instead of being unpacked into the selected one.
            if (SelectedResult is { ProjectType: ContentProjectType.Modpack })
            {
                await InstallModpackAsync(version).ConfigureAwait(true);
                return;
            }

            _shell.BeginActivity($"Installing {SelectedResult?.Title ?? version.VersionNumber}...");
            StatusNote = null;

            var installer = _services.InstallerFor(ActiveProvider);
            var plan = await installer
                .PlanAsync(
                    instance,
                    version.ProjectId,
                    version.VersionId,
                    IncludeOptionalDependencies,
                    CancellationToken.None)
                .ConfigureAwait(true);

            var gameDirectory = _services.Paths.InstanceGameDirectory(instance.Id);
            var result = await installer
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

    private async Task InstallModpackAsync(ContentVersion version)
    {
        var file = version.PrimaryFile;
        if (file is null || string.IsNullOrEmpty(file.Url))
        {
            StatusNote = "This modpack file cannot be downloaded automatically.";
            return;
        }

        var title = SelectedResult?.Title ?? version.VersionNumber;
        _shell.BeginActivity($"Downloading {title}...");
        StatusNote = null;

        Directory.CreateDirectory(_services.Paths.TemporaryDirectory);
        var archivePath = Path.Combine(
            _services.Paths.TemporaryDirectory,
            PathSafety.SanitizeFileName(file.FileName));

        var summary = await _services.Downloads
            .DownloadAsync(
                [
                    new DownloadRequest
                    {
                        Url = file.Url,
                        TargetPath = archivePath,
                        ExpectedSha1 = file.Sha1,
                        ExpectedSha512 = file.Sha512,
                        ExpectedSize = file.Size > 0 ? file.Size : null,
                        Label = Path.GetFileName(archivePath),
                    },
                ],
                DownloadProgressAdapter.Create(new Progress<InstallProgress>(_shell.ReportActivity)),
                CancellationToken.None)
            .ConfigureAwait(true);

        if (summary.Failures.Count > 0 || !File.Exists(archivePath))
        {
            StatusNote = summary.Failures.FirstOrDefault()?.Message ?? "The modpack could not be downloaded.";
            return;
        }

        var request = new ModpackInstallRequest { ArchivePath = archivePath };
        var progress = new Progress<InstallProgress>(_shell.ReportActivity);

        var result = ModpackArchives.DetectKind(archivePath) switch
        {
            ModpackArchiveKind.Modrinth => await _services.Modpacks
                .InstallAsync(request, progress, CancellationToken.None)
                .ConfigureAwait(true),
            ModpackArchiveKind.CurseForge => await _services.CurseForgePacks
                .InstallAsync(request, progress, CancellationToken.None)
                .ConfigureAwait(true),
            _ => throw new ContentProviderException(
                "That project is not a modpack archive Ferrite can install."),
        };

        StatusNote = $"Installed {result.Instance.Name}: {result.FilesDownloaded} file(s)";
        _shell.ReportStatus(StatusNote);
        await LoadInstancesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenProjectPage()
    {
        if (SelectedResult is { } project)
        {
            ShellOpen.Url(ProjectPageUrl(project));
        }
    }

    /// <summary>Builds the public project page for whichever provider produced the result.</summary>
    private static string ProjectPageUrl(ContentSummary project) => project.Provider switch
    {
        CurseForgeClient.ProviderName =>
            $"{CurseForgeIds.WebBase}/{CurseForgeIds.WebSegmentFor(project.ProjectType)}/{project.Slug}",
        _ => $"https://modrinth.com/project/{project.Slug}",
    };

    [RelayCommand]
    private async Task RefreshInstancesAsync() => await LoadInstancesAsync().ConfigureAwait(true);
}
