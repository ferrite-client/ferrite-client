using System.Text;
using Ferrite.App.Localization;
using Ferrite.App.Services;
using Ferrite.App.ViewModels;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Ferrite.App.Tests;

/// <summary>
/// Entering a provider API key in Settings: it has to reach the credential store, be protected on
/// disk, and never be written into the settings document or left sitting in the field.
/// </summary>
public sealed class SettingsKeyTests : IDisposable
{
    private const string Key = "cf-key-0123456789abcdef";

    private readonly string _root;
    private readonly AppServices _services;
    private readonly SettingsViewModel _viewModel;

    public SettingsKeyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        _services = new AppServices(loggerFactory, AppPaths.ForRoot(_root));
        Localizer.Apply(Avalonia.Application.Current, Localizer.English);
        _viewModel = new SettingsViewModel(_services, new MainWindowViewModel(_services));
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
    public async Task A_key_entered_in_settings_reaches_the_protected_store_and_leaves_the_field()
    {
        Assert.False(_services.Credentials.HasCurseForgeApiKey);
        Assert.Contains(
            "No key",
            _viewModel.CurseForgeKeyStatus,
            StringComparison.OrdinalIgnoreCase);

        _viewModel.NewCurseForgeApiKey = Key;
        await _viewModel.SaveCommand.ExecuteAsync(null);

        // The provider can now be queried, the field is cleared, and the status reflects the store.
        Assert.True(_services.Credentials.HasCurseForgeApiKey);
        Assert.Equal(Key, _services.Credentials.CurseForgeApiKey);
        Assert.Null(_viewModel.NewCurseForgeApiKey);
        Assert.Contains(
            "protected",
            _viewModel.CurseForgeKeyStatus,
            StringComparison.OrdinalIgnoreCase);

        // Nothing plaintext: not in the settings document, and not in the credential file on disk.
        var settingsText = await File.ReadAllTextAsync(_services.Paths.SettingsFile, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Key, settingsText, StringComparison.Ordinal);
        var credentialsText = Encoding.UTF8.GetString(
            await File.ReadAllBytesAsync(_services.Paths.SecretsFile, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Key, credentialsText, StringComparison.Ordinal);

        // A second store reading the same file decrypts the key, which is what a restart does.
        var reloadedStore = new ProtectedSecretStore(
            _services.Paths.SecretsFile,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProtectedSecretStore>.Instance);
        reloadedStore.Load();
        Assert.Equal(Key, new ProviderCredentialStore(reloadedStore).CurseForgeApiKey);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Clearing_the_key_removes_it_from_the_store()
    {
        _services.Credentials.CurseForgeApiKey = Key;
        _services.Credentials.Save();

        _viewModel.ClearCurseForgeKeyCommand.Execute(null);

        Assert.False(_services.Credentials.HasCurseForgeApiKey);
        var reloadedStore = new ProtectedSecretStore(
            _services.Paths.SecretsFile,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProtectedSecretStore>.Instance);
        reloadedStore.Load();
        Assert.Null(new ProviderCredentialStore(reloadedStore).CurseForgeApiKey);
    }
}
