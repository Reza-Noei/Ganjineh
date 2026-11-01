using System.IO;
using System.Text.Json;
using PasswordManager.Persistence.IO;

namespace PasswordManager.Persistence.Registry;

/// <summary>
/// Non-sensitive vault registry (vault-file-format.md §4): vault ID, path, last-opened time and
/// the header-counter high-water mark used for rollback warnings. Deliberately contains NO vault
/// names or metadata — asserted by ST-DB-4.
/// </summary>
public sealed class VaultRegistry
{
    public sealed record RegistryEntry(
        string VaultId,
        string Path,
        string? LocalLabel,
        DateTimeOffset? LastOpenedAt,
        ulong LastHeaderCounter);

    private sealed record RegistryFile(int Version, List<RegistryEntry> Vaults);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private RegistryFile _state;

    public VaultRegistry(string filePath)
    {
        _filePath = filePath;
        _state = Load();
    }

    public ulong GetHeaderCounterHighWater(string vaultId) =>
        Find(vaultId)?.LastHeaderCounter ?? 0;

    public string? GetPath(string vaultId) => Find(vaultId)?.Path;

    public IReadOnlyList<RegistryEntry> List() => _state.Vaults.ToList();

    public void Remove(string vaultId)
    {
        var entry = Find(vaultId);
        if (entry is not null)
        {
            _state.Vaults.Remove(entry);
            Save();
        }
    }

    public void SetLabel(string vaultId, string? label)
    {
        var entry = Find(vaultId);
        if (entry is null)
        {
            return;
        }

        _state.Vaults.Remove(entry);
        _state.Vaults.Add(entry with { LocalLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim() });
        Save();
    }

    /// <summary>Optional user-facing label; plain text by explicit design (this device only).</summary>
    public void RecordOpen(string vaultId, string path, ulong headerCounter, string? localLabel = null)
    {
        var entry = Find(vaultId);
        if (entry is not null)
        {
            _state.Vaults.Remove(entry);
        }

        _state.Vaults.Add(new RegistryEntry(
            vaultId,
            path,
            localLabel ?? entry?.LocalLabel,
            DateTimeOffset.UtcNow,
            Math.Max(headerCounter, entry?.LastHeaderCounter ?? 0)));
        Save();
    }

    private RegistryEntry? Find(string vaultId) =>
        _state.Vaults.FirstOrDefault(v => string.Equals(v.VaultId, vaultId, StringComparison.Ordinal));

    private RegistryFile Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return JsonSerializer.Deserialize<RegistryFile>(File.ReadAllText(_filePath)) ?? new RegistryFile(1, []);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Corrupt registry = vault list lost, never vault data; rebuild empty.
        }
        catch (IOException)
        {
        }

        return new RegistryFile(1, []);
    }

    private void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        AtomicFile.WriteAllBytes(_filePath, JsonSerializer.SerializeToUtf8Bytes(_state, JsonOptions));
    }
}
