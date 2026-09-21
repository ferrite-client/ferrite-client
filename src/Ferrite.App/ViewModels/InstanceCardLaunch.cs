using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>Launch behaviour for a library card, kept separate from its presentation state.</summary>
public sealed partial class InstanceCardViewModel
{
    [RelayCommand]
    private async Task PlayAsync()
    {
        if (IsRunning || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Preparing {Record.Name}...");
            StatusNote = null;

            var result = await InstanceLaunchFlow
                .StartAsync(_services, _shell, Record, quickPlayWorld: null, joinLastServer: false, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Started || result.Process is null)
            {
                _shell.ReportError(result.Error ?? Localizer.Get("L.Instance.LaunchFailed"));
                return;
            }

            var warning = result.Issues.FirstOrDefault(issue => !issue.IsBlocking);
            StatusNote = warning?.Message
                ?? Localizer.Format("L.Instance.RunningAs", result.Process.ProcessId);
            IsRunning = true;
            _shell.RunningInstanceName = Record.Name;
            _shell.ReportStatus(Localizer.Format("L.Instance.Running", Record.Name));

            WatchExit(result.Process);
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

    private void WatchExit(GameProcess process)
    {
        InstanceLaunchFlow.WatchExit(_services, _shell, Record, process, exitCode =>
        {
            IsRunning = false;
            StatusNote = exitCode == 0
                ? Localizer.Get("L.Instance.ExitNormal")
                : Localizer.Format("L.Instance.ExitCode", exitCode);
        });
    }
}
