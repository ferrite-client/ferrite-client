using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>LabyMod installation for an instance.</summary>
public sealed partial class InstanceDetailViewModel
{
    /// <summary>
    /// Installs LabyMod for the instance's own Minecraft version and points the instance at it. Unlike
    /// OptiFine this needs no file from the user: LabyMod publishes the metadata a launcher installs
    /// from, so the whole thing is done here.
    /// </summary>
    [RelayCommand]
    private async Task InstallLabyModAsync()
    {
        if (IsInstallingLoader)
        {
            return;
        }

        IsInstallingLoader = true;
        _shell.BeginActivity(Localizer.Get("L.Instance.LabyModInstalling"));
        try
        {
            LoaderStatus = null;
            var result = await _services.LabyMod
                .InstallAsync(
                    Record.Id,
                    Record.MinecraftVersion,
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            Record.Loader = LoaderKind.LabyMod;
            Record.LoaderVersion = result.VersionId;
            await _services.Instances.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);

            OnPropertyChanged(nameof(LoaderSummary));
            LoaderStatus = Localizer.Format(
                "L.Instance.LabyModInstalled",
                result.LabyModVersion,
                result.VersionId);
            _shell.ReportStatus(LoaderStatus);
            await _shell.Library.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            LoaderStatus = exception.Message;
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsInstallingLoader = false;
            _shell.EndActivity();
        }
    }
}
