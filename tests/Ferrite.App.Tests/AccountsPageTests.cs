using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.App.Views;
using Ferrite.Core.Auth;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.App.Tests;

/// <summary>
/// The account manager with an account in it. Signing in needs a live Microsoft account, so the stored
/// record is written the way the sign-in flow writes it and then read back through the real store.
/// </summary>
public sealed class AccountsPageTests : IDisposable
{
    private readonly string _root;
    private readonly AppServices _services;

    public AccountsPageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-accounts-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Writes an account the way the sign-in flow leaves it, without a live sign-in.</summary>
    private Guid AddStoredAccount(string name, bool active)
    {
        var secrets = new ProtectedSecretStore(
            _services.Paths.SecretsFile,
            NullLogger<ProtectedSecretStore>.Instance);
        var store = new AccountStore(
            _services.Paths,
            secrets,
            NullLogger<AccountStore>.Instance);
        // The store is not a singleton: it reads the file into memory, so a second helper call has to
        // load what the first one saved before it appends.
        store.Load();

        var account = new AccountRecord
        {
            Id = Guid.NewGuid(),
            Kind = "microsoft",
            PlayerName = name,
            Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5",
            OwnsMinecraft = true,
        };
        store.Add(account);

        // The running service has to re-read the file the way it would after a restart.
        _services.Accounts.Load();
        if (active)
        {
            _services.Settings.Current.ActiveAccountId = account.Id;
        }

        return account.Id;
    }

    [AvaloniaFact]
    public void The_account_manager_lists_a_stored_account_and_marks_the_active_one()
    {
        var activeName = "Aleksander-Kowalczuk-With-A-Long-Name";
        var activeId = AddStoredAccount(activeName, active: true);
        AddStoredAccount("Alt account", active: false);

        var shell = new MainWindowViewModel(_services);
        var viewModel = new AccountsViewModel(_services, shell);
        viewModel.Load();

        Assert.Equal(2, viewModel.Accounts.Count);
        var active = Assert.Single(viewModel.Accounts, item => item.IsActive);
        Assert.Equal(activeName, active.DisplayName);
        Assert.Equal(activeId, active.Account.Id);
        Assert.Contains("Microsoft", active.Kind, StringComparison.OrdinalIgnoreCase);

        // The rail footer follows the same account. The shell resolves its active account from its own
        // account list, so that list is loaded first - the same order InitializeAsync uses.
        shell.Accounts.Load();
        shell.RefreshActiveAccount();
        Assert.Equal(activeName, shell.ActiveAccountName);
        Assert.True(shell.HasActiveAccount);

        var window = new Window
        {
            Content = new AccountsView { DataContext = viewModel },
            Width = 1200,
            Height = 800,
        };
        window.Show();
        window.CaptureRenderedFrame();

        var texts = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        Assert.Contains(activeName, texts);
        Assert.Contains("Alt account", texts);
        Assert.Contains("ACTIVE", texts);
        Assert.Contains("Sign out", texts);
        Assert.Contains("Use", texts);

        // A long name is trimmed inside its card rather than widening the column.
        var name = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .First(block => block.Text == activeName);
        Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, name.TextTrimming);
        Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, name.TextWrapping);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("FERRITE_UI_SHOTS");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            frame!.Save(
                Path.Combine(directory, "page-accounts-filled.png"),
                new PngBitmapEncoderOptions());
        }
    }
}
