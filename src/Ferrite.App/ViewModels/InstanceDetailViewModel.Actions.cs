using CommunityToolkit.Mvvm.Input;
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
    private void Back() => _shell.DetailPage = null;

    [RelayCommand]
    private Task RefreshLog() => RefreshLogAsync();

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
            Record.JavaPath = SelectedJava?.ExecutablePath;
            Record.LastServerAddress = string.IsNullOrWhiteSpace(ServerAddress) ? null : ServerAddress.Trim();
            Record.JvmArguments = SplitArguments(JvmArgumentsText);
            Record.GameArguments = SplitArguments(GameArgumentsText);

            await _services.Instances.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);
            StatusNote = "Settings saved";
            _shell.ReportStatus($"{Name} settings saved");
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
                ? $"Verified {report.FilesChecked} files ({ByteSize.Format(report.TotalBytes)})"
                : $"{report.Issues.Count} file(s) missing or corrupt";
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
            StatusNote = report.IsHealthy ? "Installation repaired" : $"{report.Issues.Count} problem(s) remain";
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

            StatusNote = installed == 0 ? "No mod files were added" : $"Added {installed} mod file(s)";
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
