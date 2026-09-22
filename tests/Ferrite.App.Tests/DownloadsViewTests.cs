using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Diagnostics;
using Ferrite.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The downloads view, against the real operation log. The log is the same one the diagnostics
/// section reads, so these tests also pin down that the two agree about what happened.
/// </summary>
public sealed class DownloadsViewTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public DownloadsViewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-downloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The filter options carry their own values, so "all" is a value and not a label.</summary>
    [Fact]
    public void The_filter_options_pair_a_value_with_a_label()
    {
        var viewModel = new DownloadsViewModel(_services, new MainWindowViewModel(_services));

        Assert.Equal("all", viewModel.SelectedFilter.Value);
        Assert.Equal(
            new[] { "all", "succeeded", "failed" },
            viewModel.Filters.Select(filter => filter.Value).ToArray());
        Assert.All(viewModel.Filters, filter => Assert.NotEqual(filter.Value, filter.Label));
    }

    /// <summary>An empty log shows the empty state, not the "filter matched nothing" state.</summary>
    [Fact]
    public void An_empty_log_shows_the_empty_state_rather_than_a_filtered_one()
    {
        var viewModel = new DownloadsViewModel(_services, new MainWindowViewModel(_services));
        viewModel.Refresh();

        Assert.True(viewModel.ShowsEmptyHistory);
        Assert.False(viewModel.ShowsNoFilterMatches);
        Assert.False(viewModel.HasHistory);
    }

    [Fact]
    public void History_comes_from_the_operation_log_and_filters_by_outcome()
    {
        _services.Operations.Record(
            "Installing Sodium",
            OperationOutcome.Succeeded,
            detail: null,
            TimeSpan.FromSeconds(4));
        _services.Operations.Record(
            "Downloading Create",
            OperationOutcome.Failed,
            detail: "the mirror refused the request",
            TimeSpan.FromSeconds(2));

        var viewModel = new DownloadsViewModel(_services, new MainWindowViewModel(_services));
        viewModel.Refresh();

        Assert.True(viewModel.HasHistory);
        Assert.Equal(2, viewModel.History.Count);
        // Most recent first, so the failure that just happened is on top.
        Assert.Equal("Downloading Create", viewModel.History[0].Name);
        Assert.Equal("Failed", viewModel.History[0].StateText);
        Assert.True(viewModel.History[0].HasDetail);

        viewModel.SelectedFilter = viewModel.Filters.Single(filter => filter.Value == "failed");
        Assert.Equal("Downloading Create", Assert.Single(viewModel.History).Name);

        viewModel.SelectedFilter = viewModel.Filters.Single(filter => filter.Value == "succeeded");
        Assert.Equal("Installing Sodium", Assert.Single(viewModel.History).Name);

        viewModel.SelectedFilter = viewModel.Filters.Single(filter => filter.Value == "all");
        viewModel.SearchText = "sodium";
        Assert.Equal("Installing Sodium", Assert.Single(viewModel.History).Name);

        viewModel.SearchText = "nothing matches this";
        Assert.Empty(viewModel.History);
        Assert.True(viewModel.ShowsNoFilterMatches);
        Assert.False(viewModel.ShowsEmptyHistory);
    }

    /// <summary>A running operation is reported from the shell's own activity, not invented here.</summary>
    [Fact]
    public void A_running_operation_appears_as_active()
    {
        var shell = new MainWindowViewModel(_services);
        // The shell owns the instance the window binds to, so the activity it starts lands there.
        var viewModel = shell.Downloads;

        shell.BeginActivity("Downloading Minecraft 1.21.1...", indeterminate: false);
        shell.ReportActivity(new Ferrite.Core.Minecraft.InstallProgress
        {
            Stage = Ferrite.Core.Minecraft.InstallStage.Downloading,
            Message = "client.jar",
            Download = new Ferrite.Core.Download.DownloadProgress
            {
                TotalFiles = 8,
                CompletedFiles = 4,
                FailedFiles = 0,
                SkippedFiles = 0,
                TotalBytes = 8_000,
                CompletedBytes = 4_000,
                BytesPerSecond = 512_000,
                CurrentItem = "client.jar",
            },
        });

        Assert.True(viewModel.HasActive);
        Assert.Equal(1, viewModel.ActiveCount);
        Assert.True(viewModel.Active[0].IsRunning);
        Assert.Equal(0.5, viewModel.Active[0].Fraction);
        Assert.True(viewModel.Active[0].HasMeasuredProgress);

        shell.EndActivity();
        Assert.False(viewModel.HasActive);
    }
}
