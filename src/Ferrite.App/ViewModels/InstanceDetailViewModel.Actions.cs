using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;

namespace Ferrite.App.ViewModels;

/// <summary>Detail-page actions: save settings, verify, repair, and mod file installation.</summary>
public sealed partial class InstanceDetailViewModel
{
    [RelayCommand]
    private void Back()
    {
        // The LAN listener is a shared socket; leaving the page must not leave it running.
        StopLanScan();
        _shell.DetailPage = null;
    }

    [RelayCommand]
    private Task RefreshLog() => RefreshLogAsync();

    /// <summary>
    /// Applies a pack archive over this instance: the same installers that create an instance from a
    /// pack, told to target the one that is already open. Existing content is moved into the launcher's
    /// backups before the pack's files land, so an update cannot destroy what it replaces silently.
    /// </summary>
    public async Task UpdateFromArchiveAsync(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            StatusNote = Localizer.Get("L.Library.UnreadableFile");
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Updating {Name} from {Path.GetFileName(archivePath)}...");
            StatusNote = null;

            var request = new ModpackInstallRequest
            {
                ArchivePath = archivePath,
                TargetInstanceId = Record.Id,
            };
            var progress = new Progress<InstallProgress>(_shell.ReportActivity);
            var result = ModpackArchives.DetectKind(archivePath) switch
            {
                ModpackArchiveKind.Modrinth => await _services.Modpacks
                    .InstallAsync(request, progress, CancellationToken.None)
                    .ConfigureAwait(true),
                ModpackArchiveKind.CurseForge => await _services.CurseForgePacks
                    .InstallAsync(request, progress, CancellationToken.None)
                    .ConfigureAwait(true),
                _ => throw new ContentProviderException(Localizer.Get("L.Library.ModpackUnsupported")),
            };

            // The install rewrote the record's loader and pack identity, so the page has to re-read it.
            ApplyRecord(result.Instance);
            StatusNote = Localizer.Format(
                "L.Instance.UpdatedFromPack",
                result.FilesDownloaded,
                result.OverrideFiles);
            foreach (var warning in result.Warnings.Take(2))
            {
                StatusNote += " · " + warning;
            }

            _shell.ReportStatus(StatusNote);
            await RefreshContentAsync().ConfigureAwait(true);
            await _shell.Library.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// Exports this instance as a Modrinth modpack. Called by the view after the user picks a target
    /// file, so the view model never touches platform storage APIs.
    /// </summary>
    public async Task ExportModpackAsync(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Exporting {Name}...");
            var result = await _services.ModpackExporter
                .ExportAsync(Record, outputPath, includeSaves: false, CancellationToken.None)
                .ConfigureAwait(true);

            StatusNote = Localizer.Format(
                "L.Instance.Exported",
                result.OverrideCount,
                Path.GetFileName(result.ArchivePath));
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
    private void OpenFolder() => ShellOpen.Directory(GameDirectory);

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            Record.MemoryMb = MemoryMb is > 0 ? MemoryMb : null;
            Record.MinMemoryMb = MinMemoryMb is > 0 ? MinMemoryMb : null;
            Record.WindowWidth = WindowWidth is > 0 ? WindowWidth : null;
            Record.WindowHeight = WindowHeight is > 0 ? WindowHeight : null;
            Record.DemoMode = DemoMode;
            Record.AcceptEula = AcceptEula;
            Record.JavaPath = SelectedJava?.ExecutablePath;
            Record.LastServerAddress = string.IsNullOrWhiteSpace(ServerAddress) ? null : ServerAddress.Trim();
            Record.Group = InstanceManager.NormalizeGroup(Group);
            Record.JvmArguments = SplitArguments(JvmArgumentsText);
            Record.GameArguments = SplitArguments(GameArgumentsText);

            await _services.Instances.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);
            EulaFile.Apply(GameDirectory, AcceptEula);
            StatusNote = Localizer.Get("L.Instance.SettingsSaved");
            _shell.ReportStatus(StatusNote);
            await _shell.Library.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Verifying {Name}...");
            var report = await _services.Installer
                .VerifyAsync(Record.Id, VersionId, RuleContext.ForHost(), null, CancellationToken.None)
                .ConfigureAwait(true);
            StatusNote = report.IsHealthy
                ? Localizer.Format(
                    "L.Instance.Verified",
                    report.FilesChecked,
                    ByteSize.Format(report.TotalBytes))
                : Localizer.Format("L.Instance.CorruptFiles", report.Issues.Count);
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
    private async Task RepairAsync()
    {
        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Repairing {Name}...");
            var report = await _services.Installer
                .RepairAsync(
                    Record.Id,
                    VersionId,
                    RuleContext.ForHost(),
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);
            StatusNote = report.IsHealthy
                ? Localizer.Get("L.Instance.Repaired")
                : Localizer.Format("L.Instance.ProblemsRemain", report.Issues.Count);
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

    /// <summary>Copies picked or dropped mod files into the instance and rescans.</summary>
    public async Task InstallModFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var modsDirectory = Path.Combine(GameDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);
            var installed = 0;

            foreach (var path in paths)
            {
                if (!File.Exists(path) || !ModScanner.IsModCandidate(Path.GetFileName(path)))
                {
                    continue;
                }

                var target = Path.Combine(modsDirectory, PathSafety.SanitizeFileName(Path.GetFileName(path)));
                if (!PathSafety.IsContained(GameDirectory, target))
                {
                    continue;
                }

                File.Copy(path, target, overwrite: true);
                installed++;
            }

            StatusNote = installed == 0
                ? Localizer.Get("L.Instance.NoModsAdded")
                : Localizer.Format("L.Instance.AddedMods", installed);
            await RefreshModsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
    }

    private static List<string> SplitArguments(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
