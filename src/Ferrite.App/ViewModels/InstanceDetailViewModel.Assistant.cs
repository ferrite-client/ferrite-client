using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Rules;

namespace Ferrite.App.ViewModels;

/// <summary>
/// The instance assistant: gathers this instance's own evidence, runs the rule-based advisor over
/// it, and shows the result. It refreshes the log, the crash reports, and the mod list first so the
/// answer is about the instance as it is now rather than as it was when the page opened.
/// </summary>
public sealed partial class InstanceDetailViewModel
{
    [ObservableProperty]
    private string _advisorText = string.Empty;

    [ObservableProperty]
    private bool _isAdvising;

    [RelayCommand]
    private async Task AskAssistantAsync()
    {
        if (IsAdvising)
        {
            return;
        }

        IsAdvising = true;
        _shell.BeginActivity(Localizer.Get("L.Instance.AssistantRunning"));
        try
        {
            await RefreshLogAsync().ConfigureAwait(true);
            await RefreshCrashReportsAsync().ConfigureAwait(true);
            await RefreshModsAsync().ConfigureAwait(true);

            var inputs = new AdvisorInputs
            {
                Instance = Record,
                Mods = Mods.Select(item => item.Metadata).ToList(),
                CrashReports = CrashReports.ToList(),
                Preflight = await CheckManagedFilesAsync().ConfigureAwait(true),
                LogText = LogText,
                JavaAvailable = JavaRuntimes.Count > 0,
            };

            var report = await Task
                .Run(() => InstanceAdvisor.Advise(inputs), CancellationToken.None)
                .ConfigureAwait(true);
            AdvisorText = Ferrite.Core.Diagnostics.AdvisorText.Render(report, Record);
            StatusNote = report.HasProblems
                ? Localizer.Get("L.Instance.AssistantFoundProblems")
                : Localizer.Get("L.Instance.AssistantClean");
        }
        catch (Exception exception)
        {
            StatusNote = exception.Message;
        }
        finally
        {
            IsAdvising = false;
            _shell.EndActivity();
        }
    }

    /// <summary>
    /// A managed-file check, turned into the same issue shape the launch preflight produces so the
    /// advisor has one kind of input rather than two.
    /// </summary>
    private async Task<IReadOnlyList<PreflightIssue>> CheckManagedFilesAsync()
    {
        try
        {
            var report = await _services.Installer
                .VerifyAsync(Record.Id, VersionId, RuleContext.ForHost(), null, CancellationToken.None)
                .ConfigureAwait(true);
            if (report.IsHealthy || report.Issues.Count == 0)
            {
                return [];
            }

            var first = report.Issues[0];
            return
            [
                new PreflightIssue(
                    "verify-missing-files",
                    $"{report.Issues.Count} managed file(s) are missing or damaged; for example "
                    + $"{first.Path} ({first.Reason}).",
                    IsBlocking: true),
            ];
        }
        catch (Exception exception)
        {
            // A version that cannot even be planned is not a reason to lose the rest of the advice.
            StatusNote = exception.Message;
            return [];
        }
    }
}
