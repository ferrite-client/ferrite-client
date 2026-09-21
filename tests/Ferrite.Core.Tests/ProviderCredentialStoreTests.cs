using Ferrite.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ferrite.Core.Tests;

/// <summary>
/// Provider keys must never land in the plaintext settings document, so they are written through
/// the OS-protected secret store.
/// </summary>
public sealed class ProviderCredentialStoreTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _secretsFile;

    public ProviderCredentialStoreTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ferrite-creds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        _secretsFile = Path.Combine(_workspace, "secrets.bin");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_key_round_trips_through_the_protected_store()
    {
        var first = CreateStore();
        Assert.False(first.HasCurseForgeApiKey);
        Assert.Null(first.CurseForgeApiKey);

        first.CurseForgeApiKey = "  curseforge-key-value  ";
        first.Save();

        var second = CreateStore();
        Assert.True(second.HasCurseForgeApiKey);
        Assert.Equal("curseforge-key-value", second.CurseForgeApiKey);
    }

    [Fact]
    public void Clearing_a_key_removes_it()
    {
        var store = CreateStore();
        store.CurseForgeApiKey = "some-key";
        store.Save();

        store.CurseForgeApiKey = "   ";
        store.Save();

        Assert.False(store.HasCurseForgeApiKey);
        Assert.False(CreateStore().HasCurseForgeApiKey);
    }

    [Fact]
    public void The_key_is_not_stored_in_plaintext()
    {
        var store = CreateStore();
        store.CurseForgeApiKey = "plaintext-marker-value";
        store.Save();

        var raw = File.ReadAllText(_secretsFile);
        Assert.DoesNotContain("plaintext-marker-value", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_loads_do_not_discard_unsaved_entries()
    {
        var secrets = new ProtectedSecretStore(_secretsFile, NullLogger<ProtectedSecretStore>.Instance);
        var store = new ProviderCredentialStore(secrets);

        secrets.Load();
        store.CurseForgeApiKey = "kept-in-memory";
        secrets.Load();

        Assert.Equal("kept-in-memory", store.CurseForgeApiKey);
    }

    private ProviderCredentialStore CreateStore()
    {
        var secrets = new ProtectedSecretStore(_secretsFile, NullLogger<ProtectedSecretStore>.Instance);
        secrets.Load();
        return new ProviderCredentialStore(secrets);
    }
}
