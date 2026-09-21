using CommunityToolkit.Mvvm.ComponentModel;
using Ferrite.App.Localization;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>OptiFine installation for an instance.</summary>
public sealed partial class InstanceDetailViewModel
{
    [ObservableProperty]
    private string? _loaderStatus;

    [ObservableProperty]
    private bool _isInstallingLoader;

    public string LoaderSummary => Record.Loader == LoaderKind.Vanilla
        ? Localizer.Get("L.Instance.LoaderVanilla")
        : $"{Record.Loader.ToDisplayName()} {Record.LoaderVersion}";

    /// <summary>
    /// Runs OptiFine's installer for the user and adopts the result. Called by the view after the
    /// user has picked a file, so the view model never touches platform storage APIs.
    /// </summary>
    public async Task InstallOptiFineAsync(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            LoaderStatus = Localizer.Get("L.Library.UnreadableFile");
            return;
        }

        var info = OptiFineInstaller.Inspect(installerPath);
        if (info is null)
        {
            LoaderStatus = Localizer.Format(
                "L.Instance.NotAnOptiFineInstaller",
                Path.GetFileName(installerPath));
            return;
        }

        // The installer patches one Minecraft version. Adopting it into a different instance would
        // produce a version that cannot launch.
        if (!string.Equals(info.MinecraftVersion, Record.MinecraftVersion, StringComparison.OrdinalIgnoreCase))
        {
            LoaderStatus = Localizer.Format(
                "L.Instance.OptiFineVersionMismatch",
                info.MinecraftVersion,
                Record.MinecraftVersion);
            return;
        }

        IsInstallingLoader = true;
        try
        {
            var required = JavaCompatibility.RequiredMajorFor(Record.MinecraftVersion);
            var runtimes = await _services.Java.DetectAsync(CancellationToken.None).ConfigureAwait(true);
            var java = JavaSelection.SelectBest(runtimes, required);
            if (java is null)
            {
                LoaderStatus = Localizer.Format("L.Instance.OptiFineNeedsJava", required ?? 8);
                return;
            }

            var staging = Path.Combine(
                _services.Paths.TemporaryDirectory,
                "optifine-" + info.VersionId);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            _shell.BeginActivity(Localizer.Format("L.Instance.OptiFineRunning", info.DisplayName));
            LoaderStatus = Localizer.Get("L.Instance.OptiFineWaitingForInstaller");

            var exitCode = await _services.OptiFine
                .RunInstallerAsync(installerPath, staging, java, CancellationToken.None)
                .ConfigureAwait(true);
            if (exitCode != 0)
            {
                LoaderStatus = Localizer.Format("L.Instance.OptiFineInstallerFailed", exitCode);
                return;
            }

            var result = await Task
                .Run(() => _services.OptiFine.Adopt(staging, info.VersionId, CancellationToken.None),
                    CancellationToken.None)
                .ConfigureAwait(true);

            Record.Loader = LoaderKind.OptiFine;
            Record.LoaderVersion = result.VersionId;
            await _services.Instances.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);

            OnPropertyChanged(nameof(LoaderSummary));
            LoaderStatus = result.Warnings.Count == 0
                ? Localizer.Format("L.Instance.OptiFineInstalled", result.VersionId)
                : Localizer.Format("L.Instance.OptiFineInstalledWithWarnings", result.Warnings.Count);
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
