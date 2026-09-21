using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ferrite.App.Localization;
using Ferrite.Core.Content;
using Ferrite.Core.Minecraft;

namespace Ferrite.App.ViewModels;

/// <summary>One available content update, with a selection the user can turn off.</summary>
public sealed partial class ContentUpdateItemViewModel : ObservableObject
{
    public ContentUpdateItemViewModel(ContentUpdate update, string providerDisplayName)
    {
        Update = update;
        ProviderDisplayName = providerDisplayName;
    }

    public ContentUpdate Update { get; }

    public string ProviderDisplayName { get; }

    public string Name => Update.Title ?? Path.GetFileName(Update.Entry.RelativePath);

    public string Detail => $"{Update.Entry.RelativePath} → {Update.AvailableVersionName}";

    public string ProviderNote => $"{ProviderDisplayName} · update available";

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string? _note;
}

/// <summary>Content update checking and application for an instance.</summary>
public sealed partial class InstanceDetailViewModel
{
    public ObservableCollection<ContentUpdateItemViewModel> Updates { get; } = [];

    [ObservableProperty]
    private string? _updateStatus;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    public bool HasUpdates => Updates.Count > 0;

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsCheckingUpdates = true;
        try
        {
            _shell.BeginActivity("Checking installed content for updates...");
            Updates.Clear();

            var messages = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in _services.ContentProviders)
            {
                if (!provider.IsConfigured)
                {
                    messages.Add(Localizer.Format(
                        "L.Instance.SkippedProvider",
                        ContentProviderNames.DisplayNameFor(provider.Name),
                        provider.UnavailableReason ?? string.Empty));
                    continue;
                }

                var report = await _services.ContentUpdates
                    .CheckAsync(Record, provider, CancellationToken.None)
                    .ConfigureAwait(true);

                foreach (var update in report.Updates)
                {
                    if (seen.Add(update.Entry.RelativePath))
                    {
                        Updates.Add(new ContentUpdateItemViewModel(
                            update,
                            ContentProviderNames.DisplayNameFor(provider.Name)));
                    }
                }

                messages.Add(Localizer.Format(
                    "L.Instance.ProviderSummary",
                    ContentProviderNames.DisplayNameFor(provider.Name),
                    report.Updates.Count,
                    report.UpToDate.Count));
                messages.AddRange(report.Warnings);
            }

            if (Updates.Count == 0 && messages.Count == 0)
            {
                messages.Add(Localizer.Get("L.Instance.NoTrackedContent"));
            }

            UpdateStatus = string.Join(" · ", messages);
            OnPropertyChanged(nameof(HasUpdates));
        }
        catch (Exception exception)
        {
            _shell.ReportError(exception.Message);
            UpdateStatus = exception.Message;
        }
        finally
        {
            IsCheckingUpdates = false;
            _shell.EndActivity();
        }
    }

    [RelayCommand]
    private async Task ApplyUpdatesAsync()
    {
        var selected = Updates.Where(item => item.IsSelected).Select(item => item.Update).ToList();
        if (selected.Count == 0)
        {
            UpdateStatus = Localizer.Get("L.Instance.SelectUpdate");
            return;
        }

        IsBusy = true;
        try
        {
            _shell.BeginActivity($"Updating {selected.Count} file(s)...");
            var result = await _services.ContentUpdates
                .ApplyAsync(
                    Record,
                    selected,
                    new Progress<InstallProgress>(_shell.ReportActivity),
                    CancellationToken.None)
                .ConfigureAwait(true);

            var note = Localizer.Format("L.Instance.Updated", result.Updated);
            if (result.Warnings.Count > 0)
            {
                note += $"; {result.Warnings.Count} warning(s): {string.Join("; ", result.Warnings.Take(3))}";
            }

            UpdateStatus = note;
            _shell.ReportStatus($"{Name}: {note}");
            await RefreshModsAsync().ConfigureAwait(true);
            await CheckUpdatesAsync().ConfigureAwait(true);
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
}
