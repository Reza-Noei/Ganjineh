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
            Migrations.SchemaMigrator.MigrateToCurrent(new VaultDatabase(connection, vaultId, path));
            using var tx = connection.BeginTransaction();
            SetMeta(connection, "vault_id", vaultId);
            SetMeta(connection, "schema_version", BitConverter.GetBytes(Migrations.SchemaMigrator.CurrentVersion));
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

            var database = new VaultDatabase(connection, expectedVaultId, path);
            Migrations.SchemaMigrator.MigrateToCurrent(database);
            return database;
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

    /// <summary>Stores an application-encrypted metadata blob (e.g. the vault display name envelope).</summary>
    public void SetEncryptedMeta(string key, byte[] envelope) => SetMeta(_connection, key, envelope);

    public byte[]? GetEncryptedMeta(string key) =>
        _connection.ScalarBlob($"SELECT value FROM app_meta WHERE key = '{key}';");

    /// <summary>Connection access for same-assembly repositories (key-free envelope storage).</summary>
    internal SqlCipherConnection Connection => _connection;

    /// <summary>Runs PRAGMA integrity_check; 'ok' or throws DatabaseCorruptionException.</summary>
    public void VerifyIntegrity()
    {
        var result = _connection.ScalarText("PRAGMA integrity_check;");
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseCorruptionException("Vault database failed the integrity check.");
        }
    }

    /// <summary>Long scalar helper for count-style queries.</summary>
    public long ScalarLong(string sql)
    {
        var text = _connection.ScalarText(sql);
        return long.TryParse(text, out var value) ? value : 0;
    }

    /// <summary>Consistent committed snapshot (Phase 6 backups). No active transaction may exist.</summary>
    public void VacuumInto(string path) =>
        _connection.Execute($"VACUUM INTO '{path.Replace("'", "''")}';");

    public void CheckpointWal()
    {
        _connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
    }

    public void Dispose() => _connection.Dispose();
}
