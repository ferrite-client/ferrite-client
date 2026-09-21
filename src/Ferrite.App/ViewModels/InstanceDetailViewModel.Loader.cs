using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;

namespace Ferrite.App.ViewModels;

/// <summary>
/// Changing the mod loader a loader instance runs. The loader version is installed into the store
/// first and the instance is only pointed at it once that succeeded, so a failed install cannot leave
/// an instance naming a version that does not exist.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];

    [ObservableProperty]
    private LoaderVersionInfo? _selectedLoaderVersion;

    [ObservableProperty]
    private string? _loaderSwitchStatus;

    [ObservableProperty]
    private bool _isLoaderSwitchBusy;

    /// <summary>True for an instance that actually has a mod loader to change.</summary>
    public bool CanChangeLoader => Record.Loader != LoaderKind.Vanilla;

    /// <summary>Loads the published loader versions for this instance's Minecraft version.</summary>
    public async Task LoadLoaderVersionsAsync()
    {
        if (!CanChangeLoader)
        {
            LoaderSwitchStatus = Localizer.Get("L.Instance.NoLoaderToChange");
            return;
        }

        IsLoaderSwitchBusy = true;
        try
        {
            var versions = await ListLoaderVersionsAsync(Record.Loader, Record.MinecraftVersion)
                .ConfigureAwait(true);
            LoaderVersions.Clear();
            foreach (var version in versions)
            {
                LoaderVersions.Add(version);
            }

            // The instance's own version is preselected, so the control says where it currently is.
            SelectedLoaderVersion = LoaderVersions.FirstOrDefault(version =>
                    string.Equals(version.Version, Record.LoaderVersion, StringComparison.OrdinalIgnoreCase))
                ?? LoaderVersions.FirstOrDefault(version => version.Stable);
            LoaderSwitchStatus = LoaderVersions.Count == 0
                ? Localizer.Format("L.Instance.NoLoaderBuilds", Record.MinecraftVersion)
                : Localizer.Format("L.Instance.LoaderBuilds", LoaderVersions.Count);
        }
        catch (Exception exception)
        {
            LoaderSwitchStatus = exception.Message;
        }
        finally
        {
            IsLoaderSwitchBusy = false;
        }
    }

    private async Task<IReadOnlyList<LoaderVersionInfo>> ListLoaderVersionsAsync(
        LoaderKind loader,
        string minecraftVersion) => loader switch
    {
        LoaderKind.Fabric => await _services.Fabric
            .ListFabricLoadersAsync(minecraftVersion, CancellationToken.None)
            .ConfigureAwait(true),
        LoaderKind.Quilt => await _services.Fabric
            .ListQuiltLoadersAsync(minecraftVersion, CancellationToken.None)
            .ConfigureAwait(true),
        LoaderKind.NeoForge => await _services.Forge
            .ListNeoForgeAsync(minecraftVersion, CancellationToken.None)
            .ConfigureAwait(true),
        LoaderKind.Forge => await _services.Forge
            .ListForgeAsync(minecraftVersion, CancellationToken.None)
            .ConfigureAwait(true),
        _ => [],
    };

    [RelayCommand]
    private Task ApplyLoaderVersionAsync() =>
        ApplyLoaderVersionAsync(SelectedLoaderVersion, SwitchLoaderAsync);

    /// <summary>
    /// The switch, with the install step passed in so the order can be tested without a network. The
    /// instance is updated only after the install returns; anything else leaves it pointing at a
    /// version that may not exist.
    /// </summary>
    internal async Task<bool> ApplyLoaderVersionAsync(
        LoaderVersionInfo? version,
        Func<LoaderVersionInfo, Task> apply)
    {
        if (!CanChangeLoader || version is null)
        {
            return false;
        }

        if (string.Equals(version.Version, Record.LoaderVersion, StringComparison.OrdinalIgnoreCase))
        {
            LoaderSwitchStatus = Localizer.Format("L.Instance.LoaderAlreadyCurrent", version.Version);
            return false;
        }

        IsLoaderSwitchBusy = true;
        try
        {
            _shell.BeginActivity(
                $"Installing {Record.Loader.ToDisplayName()} {version.Version}...");
            // The install and the record update both happen inside the switcher, which is what makes
            // the order - install, lay out, then point the instance at it - one decision.
            await apply(version).ConfigureAwait(true);
            ApplyRecord(Record);

            LoaderSwitchStatus = Localizer.Format(
                "L.Instance.LoaderSwitched",
                Record.Loader.ToDisplayName(),
                version.Version);
            _shell.ReportStatus(LoaderSwitchStatus);
            await RefreshContentAsync().ConfigureAwait(true);
            await _shell.Library.RefreshAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception)
        {
            // The instance still names the loader version it was running before the attempt.
            LoaderSwitchStatus = exception.Message;
            _shell.ReportError(exception.Message);
            return false;
        }
        finally
        {
            IsLoaderSwitchBusy = false;
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// Installs the loader into the store, lays out the instance for that version, and only then
    /// points the instance at it. The work lives in Core so the verification harness exercises the
    /// same sequence this page does.
    /// </summary>
    private async Task SwitchLoaderAsync(LoaderVersionInfo version) =>
        await _services.LoaderSwitcher
            .SwitchAsync(
                Record,
                version,
                new Progress<InstallProgress>(_shell.ReportActivity),
                CancellationToken.None)
            .ConfigureAwait(true);
}
