using System.Text;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The log tab against real log files, including one far larger than anything worth holding in memory.
/// </summary>
public sealed class InstanceLogTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;
    private readonly string _logsDirectory;
    private readonly InstanceDetailViewModel _viewModel;

    public InstanceLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);

        var record = _services.Instances
            .CreateAsync(
                new InstanceRecord { Id = Guid.NewGuid(), Name = "Logs", MinecraftVersion = "1.21.1" },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        _logsDirectory = Path.Combine(_services.Paths.InstanceGameDirectory(record.Id), "logs");
        Directory.CreateDirectory(_logsDirectory);
        _viewModel = new InstanceDetailViewModel(record, _services, new MainWindowViewModel(_services));
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

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task A_huge_log_is_read_as_a_bounded_tail()
    {
        const int lines = 200_000;
        var path = Path.Combine(_logsDirectory, "latest.log");
        await using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)))
        {
            for (var index = 0; index < lines; index++)
            {
                await writer.WriteLineAsync($"[{index:D7}] a log line long enough to be realistic");
            }
        }

        Assert.True(new FileInfo(path).Length > 5_000_000, "the fixture should be several megabytes");

        await _viewModel.RefreshLogAsync();

        var text = _viewModel.LogText;
        var returned = text.Split(Environment.NewLine);
        Assert.Equal(400, returned.Length);
        // The last 400 of the 200,000 lines, in order.
        Assert.StartsWith("[0199600]", returned[0], StringComparison.Ordinal);
        Assert.Contains("[0199999]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[0000000]", text, StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_newest_log_is_the_one_shown()
    {
        var older = Path.Combine(_logsDirectory, "ferrite-20260101-000000.log");
        var newer = Path.Combine(_logsDirectory, "ferrite-20260102-000000.log");
        await File.WriteAllTextAsync(older, "oldest marker", new UTF8Encoding(false), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(newer, "newest marker", new UTF8Encoding(false), TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        await _viewModel.RefreshLogAsync();

        Assert.Contains("newest marker", _viewModel.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("oldest marker", _viewModel.LogText, StringComparison.Ordinal);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task An_instance_with_no_logs_says_so()
    {
        await _viewModel.RefreshLogAsync();

        Assert.Equal(Localizer.Get("L.Instance.NoLogs"), _viewModel.LogText);
    }
}
