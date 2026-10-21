namespace PasswordManager.Persistence.SqlCipher;

/// <summary>
/// Minimal v1 persistence schema proving real encrypted storage (full item CRUD is Phase 3):
/// app_meta carries the vault identity, schema and format versions.
/// </summary>
public sealed class VaultDatabase : IDisposable
{
    public const int SchemaVersion = 1;

    private readonly SqlCipherConnection _connection;

    private VaultDatabase(SqlCipherConnection connection, byte[] vaultId, string path)
    {
        _connection = connection;
        VaultId = vaultId;
        Path = path;
    }

    public byte[] VaultId { get; }

    public string Path { get; }

    public static VaultDatabase Create(string path, ReadOnlySpan<byte> dbKey, byte[] vaultId, long createdAtUnix)
    {
        var connection = SqlCipherConnection.OpenOrCreate(path, dbKey, create: true);
        try
        {
            connection.Execute(
                "CREATE TABLE IF NOT EXISTS app_meta (key TEXT PRIMARY KEY, value BLOB NOT NULL);");
            using var tx = connection.BeginTransaction();
            SetMeta(connection, "vault_id", vaultId);
            SetMeta(connection, "schema_version", BitConverter.GetBytes(SchemaVersion));
            SetMeta(connection, "format_version", BitConverter.GetBytes((short)1));
            SetMeta(connection, "created_at_unix", BitConverter.GetBytes(createdAtUnix));
            tx.Commit();
            connection.VerifyIntegrity();
            return new VaultDatabase(connection, vaultId, path);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static VaultDatabase Open(string path, ReadOnlySpan<byte> dbKey, byte[] expectedVaultId)
    {
        var connection = SqlCipherConnection.OpenOrCreate(path, dbKey, create: false);
        try
        {
            connection.VerifyIntegrity();
            var storedId = connection.ScalarBlob("SELECT value FROM app_meta WHERE key = 'vault_id';")
                ?? throw new DatabaseCorruptionException("Vault database does not carry an identity.");
            if (!storedId.AsSpan().SequenceEqual(expectedVaultId))
            {
                throw new VaultIdentityMismatchException("Vault database identity does not match its header.");
            }

            return new VaultDatabase(connection, expectedVaultId, path);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void SetMeta(SqlCipherConnection connection, string key, byte[] value)
    {
        // Key is a compile-time literal; value is hex-encoded to keep the wrapper minimal and safe.
        var hex = SqlCipherConnection.ToHex(value);
        connection.Execute($"INSERT OR REPLACE INTO app_meta (key, value) VALUES ('{key}', x'{hex}');");
    }

    public void CheckpointWal()
    {
        _connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
    }

    public void Dispose() => _connection.Dispose();
}
