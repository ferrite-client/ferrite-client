namespace Ferrite.Core.Storage;

/// <summary>
/// Typed access to third-party provider credentials. Values live in the OS-protected secret store
/// and never in the plaintext settings document.
/// </summary>
public sealed class ProviderCredentialStore
{
    private const string CurseForgeApiKeyEntry = "provider.curseforge.api-key";

    private readonly ProtectedSecretStore _secrets;

    public ProviderCredentialStore(ProtectedSecretStore secrets)
    {
        _secrets = secrets;
    }

    /// <summary>Null when no key has been stored.</summary>
    public string? CurseForgeApiKey
    {
        get => _secrets.Get(CurseForgeApiKeyEntry);
        set => _secrets.Set(CurseForgeApiKeyEntry, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public bool HasCurseForgeApiKey => !string.IsNullOrWhiteSpace(CurseForgeApiKey);

    /// <summary>True when the platform stores secrets without OS-backed protection.</summary>
    public bool IsDegraded => _secrets.IsDegraded;

    public void Save() => _secrets.Save();
}
