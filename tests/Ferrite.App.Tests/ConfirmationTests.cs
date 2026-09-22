using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// The one blocking decision Ferrite asks for. What matters is not that a dialog appears but that the
/// action it guards has not happened yet when it appears, and does not happen when it is dismissed.
/// </summary>
public sealed class ConfirmationTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public ConfirmationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-confirm-" + Guid.NewGuid().ToString("N"));
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

    private async Task<(MainWindowViewModel Shell, LibraryViewModel Library)> SeedAsync(string name)
    {
        var record = await _services.Instances
            .CreateAsync(
                new InstanceRecord { Id = Guid.NewGuid(), Name = name, MinecraftVersion = "1.21.1" },
                CancellationToken.None)
            .ConfigureAwait(true);
        Directory.CreateDirectory(_services.Paths.InstanceGameDirectory(record.Id));

        var shell = new MainWindowViewModel(_services);
        await shell.InitializeAsync().ConfigureAwait(true);
        return (shell, shell.Library);
    }

    /// <summary>
    /// Cancelling has to leave the instance exactly where it was. A dialog that appears after the
    /// work is done would pass a screenshot review and fail the user.
    /// </summary>
    [AvaloniaFact]
    public async Task Deleting_an_instance_asks_first_and_does_nothing_when_dismissed()
    {
        var (shell, library) = await SeedAsync("Keep me");
        var card = library.Instances.Single();

        var deletion = card.DeleteCommand.ExecuteAsync(null);

        Assert.True(shell.IsConfirming);
        Assert.NotNull(shell.Confirmation);
        Assert.True(shell.Confirmation!.IsDestructive);
        Assert.Contains("Keep me", shell.Confirmation.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Delete this instance?",
            shell.Confirmation.Title,
            StringComparison.Ordinal);

        // Nothing has happened yet: the instance is still there while the question is open.
        var beforeAnswer = await _services.Instances.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Single(beforeAnswer);

        shell.Confirmation.CancelCommand.Execute(null);
        await deletion;

        Assert.False(shell.IsConfirming);
        var afterCancel = await _services.Instances.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Single(afterCancel);
    }

    /// <summary>Confirming performs the action and clears the dialog.</summary>
    [AvaloniaFact]
    public async Task Confirming_deletes_the_instance()
    {
        var (shell, library) = await SeedAsync("Delete me");
        var card = library.Instances.Single();

        var deletion = card.DeleteCommand.ExecuteAsync(null);
        Assert.True(shell.IsConfirming);

        shell.Confirmation!.ConfirmCommand.Execute(null);
        await deletion;

        Assert.False(shell.IsConfirming);
        var remaining = await _services.Instances.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(remaining);
        // Deleting keeps the files: the outcome is recoverable, which is what the dialog promises.
        Assert.True(Directory.Exists(_services.Paths.BackupsDirectory));
    }

    /// <summary>A second question supersedes the first rather than stacking dialogs.</summary>
    [AvaloniaFact]
    public async Task A_second_request_supersedes_the_first()
    {
        var shell = new MainWindowViewModel(_services);

        var first = shell.ConfirmAsync("First", "first question", "Yes");
        Assert.True(shell.IsConfirming);

        var second = shell.ConfirmAsync("Second", "second question", "Yes");
        var firstAnswer = await first;
        Assert.False(firstAnswer);
        Assert.Equal("Second", shell.Confirmation!.Title);

        shell.Confirmation.ConfirmCommand.Execute(null);
        Assert.True(await second);
        Assert.False(shell.IsConfirming);
    }

    /// <summary>The dialog is a real screen, so it is rendered and looked at like any other.</summary>
    [AvaloniaFact]
    public async Task The_confirmation_dialog_renders_over_the_page()
    {
        var (shell, _) = await SeedAsync("Rendered instance");
        _ = shell.ConfirmAsync(
            Localizer.Get("L.Confirm.DeleteInstanceTitle"),
            Localizer.Format("L.Confirm.DeleteInstanceMessage", "Rendered instance"),
            Localizer.Get("L.Confirm.DeleteInstanceConfirm"));

        var window = new MainWindow { DataContext = shell, Width = 1360, Height = 860 };
        window.Show();

        window.CaptureRenderedFrame();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var texts = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        Assert.Contains("Delete this instance?", texts);
        Assert.Contains("Cancel", texts);
        Assert.Contains("Delete instance", texts);

        var directory = Environment.GetEnvironmentVariable("FERRITE_UI_SHOTS");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            frame!.Save(
                Path.Combine(directory, "shell-confirmation.png"),
                new PngBitmapEncoderOptions());
        }
    }
}
