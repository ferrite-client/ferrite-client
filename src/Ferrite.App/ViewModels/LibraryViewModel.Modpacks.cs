using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>Modpack import for the library.</summary>
public sealed partial class LibraryViewModel
{
    [ObservableProperty]
    private string? _modpackStatus;

    /// <summary>
    /// Installs a modpack from a local archive, choosing the Modrinth or CurseForge installer from
    /// the archive's root entry. Called by the view once the user has picked a file, so the view
    /// model never touches platform storage APIs.
    /// </summary>
    public async Task ImportModpackAsync(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            ModpackStatus = Localizer.Get("L.Library.UnreadableFile");
            return;
        }

        IsBusy = true;
        try
        {
            var request = new ModpackInstallRequest { ArchivePath = archivePath };
            var progress = new Progress<InstallProgress>(_shell.ReportActivity);

            var result = ModpackArchives.DetectKind(archivePath) switch
            {
                ModpackArchiveKind.Modrinth => await InstallModrinthPackAsync(archivePath, request, progress)
                    .ConfigureAwait(true),
                ModpackArchiveKind.CurseForge => await InstallCurseForgePackAsync(archivePath, request, progress)
                    .ConfigureAwait(true),
                _ => throw new ContentProviderException(Localizer.Get("L.Library.ModpackUnsupported")),
            };

            ModpackStatus = Localizer.Format(
                "L.Library.ModpackStatus",
                result.Instance.Name,
                result.FilesDownloaded,
                result.OverrideFiles);
            if (result.Warnings.Count > 0)
            {
                ModpackStatus += " · " + Localizer.Format("L.Browse.Warnings", result.Warnings.Count);
            }

            _shell.ReportStatus(ModpackStatus);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ModpackStatus = exception.Message;
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }

    private async Task<ModpackInstallResult> InstallModrinthPackAsync(
        string archivePath,
        ModpackInstallRequest request,
        IProgress<InstallProgress> progress)
    {
        var index = MrpackInstaller.ReadIndex(archivePath);
        _shell.BeginActivity(Localizer.Format("L.Library.Installing", index.Name));
        return await _services.Modpacks
            .InstallAsync(request, progress, CancellationToken.None)
            .ConfigureAwait(true);
    }

    private async Task<ModpackInstallResult> InstallCurseForgePackAsync(
        string archivePath,
        ModpackInstallRequest request,
        IProgress<InstallProgress> progress)
    {
        var manifest = CurseForgePackInstaller.ReadManifest(archivePath);
        _shell.BeginActivity(Localizer.Format("L.Library.Installing", manifest.Name));
        return await _services.CurseForgePacks
            .InstallAsync(request, progress, CancellationToken.None)
            .ConfigureAwait(true);
    }
}
