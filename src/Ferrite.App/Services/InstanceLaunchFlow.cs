using Ferrite.App.Localization;
using Ferrite.App.ViewModels;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.App.Services;

/// <summary>
/// Starting an instance, wherever it is started from: the library card's Play button, or a world's own
/// Play button on the detail page. Kept in one place so both agree on the account, the instance's own
/// launch settings, and what happens when the game exits.
/// </summary>
internal static class InstanceLaunchFlow
{
    /// <summary>Launches, or returns the result describing why it could not.</summary>
    public static async Task<InstanceLaunchResult> StartAsync(
        AppServices services,
        MainWindowViewModel shell,
        InstanceRecord record,
        string? quickPlayWorld,
        bool joinLastServer,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(services, record).ConfigureAwait(true);
        return await services.InstanceLauncher
            .LaunchAsync(
                new InstanceLaunchRequest
                {
                    Instance = record,
                    Account = account,
                    QuickPlayWorld = quickPlayWorld,
                    JoinLastServer = joinLastServer,
                    DefaultJavaPath = services.Settings.Current.DefaultJavaPath,
                    CustomJavaPaths = services.Settings.Current.CustomJavaPaths,
                },
                new Progress<InstallProgress>(shell.ReportActivity),
                cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>Clears the running state and records the session when the game exits.</summary>
    public static void WatchExit(
        AppServices services,
        MainWindowViewModel shell,
        InstanceRecord record,
        GameProcess process,
        Action<int>? onExited = null)
    {
        _ = Task.Run(async () =>
        {
            var exitCode = await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                shell.RunningInstanceName = null;
                shell.ReportStatus(Localizer.Format("L.Instance.Stopped", record.Name));
                record.LastLaunchedAt = DateTimeOffset.UtcNow;
                record.TotalPlayTimeSeconds += (long)process.Duration.TotalSeconds;
                await services.Instances.SaveAsync(record, CancellationToken.None).ConfigureAwait(true);
                onExited?.Invoke(exitCode);
            });
        });
    }

    private static async Task<LaunchAccount?> ResolveAccountAsync(AppServices services, InstanceRecord record)
    {
        var accountId = record.AccountId ?? services.Settings.Current.ActiveAccountId;
        if (accountId is { } id)
        {
            var launchAccount = await services.Accounts
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
