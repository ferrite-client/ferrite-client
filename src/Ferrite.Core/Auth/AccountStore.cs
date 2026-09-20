using System.Text.Json;
using Ferrite.Core.Json;
using Ferrite.Core.Platform;
using Ferrite.Core.Storage;
using Ferrite.Core.Util;
using Microsoft.Extensions.Logging;

namespace Ferrite.Core.Auth;

/// <summary>Persists the account list and keeps credentials in the OS-protected secret store.</summary>
public sealed class AccountStore
{
    private readonly AppPaths _paths;
    private readonly ProtectedSecretStore _secrets;
    private readonly ILogger<AccountStore> _logger;

    public AccountStore(AppPaths paths, ProtectedSecretStore secrets, ILogger<AccountStore> logger)
    {
        _paths = paths;
        _secrets = secrets;
        _logger = logger;
    }

    public IReadOnlyList<AccountRecord> Accounts { get; private set; } = [];

    public bool IsSecretStorageDegraded => _secrets.IsDegraded;

    public void Load()
    {
        _secrets.Load();
        if (!File.Exists(_paths.AccountsFile))
        {
            Accounts = [];
            return;
        }

        try
        {
            var json = File.ReadAllBytes(_paths.AccountsFile);
            Accounts = JsonSerializer.Deserialize<List<AccountRecord>>(json, JsonDefaults.Document) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "The account list could not be read; starting with no accounts");
            Accounts = [];
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Accounts, JsonDefaults.Document);
        AtomicFile.WriteAllText(_paths.AccountsFile, json);
        _secrets.Save();
    }

    public AccountRecord? Find(Guid id) => Accounts.FirstOrDefault(account => account.Id == id);

    public void Add(AccountRecord account)
    {
        var list = Accounts.ToList();
        list.RemoveAll(existing => existing.Id == account.Id);
        list.Add(account);
        Accounts = list;
        Save();
    }

    public void Remove(Guid id)
    {
        Accounts = Accounts.Where(account => account.Id != id).ToList();
        _secrets.Remove(SecretKey(id, "msa-access"));
        _secrets.Remove(SecretKey(id, "msa-refresh"));
        _secrets.Remove(SecretKey(id, "mc-access"));
        Save();
    }

    public string? GetSecret(Guid id, string name) => _secrets.Get(SecretKey(id, name));

    public void SetSecret(Guid id, string name, string? value) => _secrets.Set(SecretKey(id, name), value);

    private static string SecretKey(Guid id, string name) => $"account:{id:D}:{name}";
}
