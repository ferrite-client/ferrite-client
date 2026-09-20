using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>Modpack import for the library.</summary>
public sealed partial class LibraryViewModel
{
    [ObservableProperty]
    private string? _modpackStatus;

    /// <summary>
    /// Installs a Modrinth modpack from a local archive. Called by the view once the user has picked
    /// a file, so the view model never touches platform storage APIs.
    /// </summary>
    public async Task ImportModpackAsync(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            ModpackStatus = "That file could not be read.";
            return;
        }

        IsBusy = true;
        try
        {
            var index = _services.Modpacks.ReadIndex(archivePath);
            ModpackStatus = $"Installing {index.Name} {index.VersionId}...";
            _shell.BeginActivity($"Installing {index.Name}...");

            var result = await _services.Modpacks
                .InstallAsync(
                    new ModpackInstallRequest { ArchivePath = archivePath },
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            ModpackStatus =
                $"{result.Instance.Name}: {result.FilesDownloaded} file(s), {result.OverrideFiles} override(s)";
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
}
