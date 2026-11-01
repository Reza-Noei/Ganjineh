using PasswordManager.Persistence;
using PasswordManager.Persistence.Registry;

namespace PasswordManager.Application.Vaults;

/// <summary>
/// Registered-vault catalog (application level): the non-sensitive registry view plus destructive
/// operations. The authoritative encrypted vault name lives inside each vault's database; the
/// optional plain-text local label is this-device-only and flagged as such in the UI.
/// </summary>
public interface IVaultCatalog
{
    IReadOnlyList<VaultSummary> List();
    void RemoveFromList(string vaultId);
    void SetLocalLabel(string vaultId, string? label);
    /// <summary>Deletes the vault directory and its registry entry. NOT a secure-erase (SSD remanence, R-6).</summary>
    void DeleteVault(string vaultId);
}

public sealed record VaultSummary(
    string VaultId,
    string Path,
    string? LocalLabel,
    DateTimeOffset? LastOpenedAt);

public sealed class VaultCatalog : IVaultCatalog
{
    private readonly Func<VaultRegistry> _registryFactory;

    public VaultCatalog(Func<VaultRegistry>? registryFactory = null) =>
        _registryFactory = registryFactory ?? VaultCatalogDefaultRegistry;

    private static VaultRegistry VaultCatalogDefaultRegistry() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ganjineh", "vaults.json"));

    public IReadOnlyList<VaultSummary> List()
    {
        var registry = _registryFactory();
        return registry.List()
            .OrderByDescending(v => v.LastOpenedAt)
            .Select(v => new VaultSummary(v.VaultId, v.Path, v.LocalLabel, v.LastOpenedAt))
            .ToList();
    }

    public void RemoveFromList(string vaultId) => _registryFactory().Remove(vaultId);

    public void SetLocalLabel(string vaultId, string? label) => _registryFactory().SetLabel(vaultId, label);

    public void DeleteVault(string vaultId)
    {
        var registry = _registryFactory();
        var path = registry.GetPath(vaultId);
        if (path is not null && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        registry.Remove(vaultId);
    }
}
