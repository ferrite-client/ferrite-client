using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The detail page's own launch state, so the instance can be started from its hero rather than only
/// from the library card. It goes through the same launch flow, so the account, the instance's launch
/// settings, and the exit handling are identical whichever button was pressed.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>True from the moment Play is pressed until the process is running or the launch fails.</summary>
    [ObservableProperty]
    private bool _isLaunching;

    /// <summary>The shell, so the hero can show the live operation the launcher is performing.</summary>
    public MainWindowViewModel Shell => _shell;

    /// <summary>The modpack this instance came from, when it came from one.</summary>
    public string PackText => Record.Modpack is { } pack
        ? $"{pack.Name} {pack.VersionName}".Trim()
        : string.Empty;

    public bool HasPack => Record.Modpack is not null;

    public string FolderText => Record.Group is { Length: > 0 } group
        ? Localizer.Format("L.Library.FolderChip", group)
        : string.Empty;

    public bool HasFolder => Record.Group is { Length: > 0 };

    /// <summary>Where the instance is now, in one line: what the hero says under the name.</summary>
    public string HeroStateText => IsRunning
        ? Localizer.Format("L.Instance.Running", Name)
        : IsLaunching
            ? Localizer.Get("L.Instance.Launching")
            : Localizer.Get("L.Instance.ReadyToPlay");

    /// <summary>Refreshes the hero after a launch starts, ends, or the record changes.</summary>
    public void RefreshHero()
    {
        OnPropertyChanged(nameof(PackText));
        OnPropertyChanged(nameof(HasPack));
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(HasFolder));
        OnPropertyChanged(nameof(HeroStateText));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(HeroStateText));
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnIsLaunchingChanged(bool value)
    {
        OnPropertyChanged(nameof(HeroStateText));
        OnPropertyChanged(nameof(IsIdle));
    }

    /// <summary>True when the hero should offer Play rather than a progress state or Stop.</summary>
    public bool IsIdle => !IsRunning && !IsLaunching;

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (IsRunning || IsLaunching)
        {
            return;
        }

        IsLaunching = true;
        try
        {
            _shell.BeginActivity($"Preparing {Name}...");
            StatusNote = null;

            var result = await InstanceLaunchFlow
                .StartAsync(_services, _shell, Record, quickPlayWorld: null, joinLastServer: false, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Started || result.Process is null)
            {
                _shell.ReportError(result.Error ?? Localizer.Get("L.Instance.LaunchFailed"));
                return;
            }

            IsRunning = true;
            _shell.RunningInstanceName = Name;
            StatusNote = result.Issues.FirstOrDefault(issue => !issue.IsBlocking)?.Message
                ?? Localizer.Format("L.Instance.RunningAs", result.Process.ProcessId);
            _shell.ReportStatus(Localizer.Format("L.Instance.Running", Name));

            InstanceLaunchFlow.WatchExit(_services, _shell, Record, result.Process, exitCode =>
            {
                IsRunning = false;
                StatusNote = exitCode == 0
                    ? Localizer.Get("L.Instance.ExitNormal")
                    : Localizer.Format("L.Instance.ExitCode", exitCode);
                RefreshHero();
            });
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
        }
        finally
        {
            IsLaunching = false;
            _shell.EndActivity();
            RefreshHero();
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _services.Launcher
            .StopAsync(Record.Id, TimeSpan.FromSeconds(15), CancellationToken.None)
            .ConfigureAwait(true);
    }
}
