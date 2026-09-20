using CommunityToolkit.Mvvm.Input;
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

            var account = await ResolveAccountAsync().ConfigureAwait(true);
            var result = await _services.InstanceLauncher
                .LaunchAsync(
                    new InstanceLaunchRequest
                    {
                        Instance = Record,
                        Account = account,
                    },
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Started || result.Process is null)
            {
                _shell.ReportError(result.Error ?? "The instance could not be started.");
                return;
            }

            var warning = result.Issues.FirstOrDefault(issue => !issue.IsBlocking);
            StatusNote = warning?.Message ?? $"Running as process {result.Process.ProcessId}";
            IsRunning = true;
            _shell.RunningInstanceName = Record.Name;
            _shell.ReportStatus($"{Record.Name} is running");

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
        _ = Task.Run(async () =>
        {
            var exitCode = await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                IsRunning = false;
                StatusNote = exitCode == 0 ? "Exited normally" : $"Exited with code {exitCode}";
                Record.LastLaunchedAt = DateTimeOffset.UtcNow;
                Record.TotalPlayTimeSeconds += (long)process.Duration.TotalSeconds;
                _shell.RunningInstanceName = null;
                _shell.ReportStatus($"{Record.Name} stopped");
                await _services.Instances.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);
            });
        });
    }

    private async Task<LaunchAccount?> ResolveAccountAsync()
    {
        var accountId = Record.AccountId ?? _services.Settings.Current.ActiveAccountId;
        if (accountId is { } id)
        {
            var launchAccount = await _services.Accounts
                .GetLaunchAccountAsync(id, CancellationToken.None)
                .ConfigureAwait(true);
            if (launchAccount is not null)
            {
                return launchAccount;
            }
        }

        return null;
    }
}
