using System.Buffers.Binary;
using System.Text;
using SQLitePCL;

namespace Ganjineh.Persistence.SqlCipher;

/// <summary>
/// Minimal raw-SQLitePCL wrapper over the genuine SQLCipher provider (e_sqlcipher).
/// Microsoft.Data.Sqlite is deliberately NOT used: its bundled e_sqlite3 battery races the
/// e_sqlcipher bundle and can silently yield an unencrypted engine. Here the provider is set
/// explicitly, once, before any connection.
/// </summary>
public sealed class SqlCipherConnection : IDisposable
{
    private static bool _providerSet;

    private sqlite3? _db;
    private bool _disposed;

    private SqlCipherConnection(sqlite3 db, string path) => (_db, Path) = (db, path);

    public string Path { get; }

    public static SqlCipherConnection OpenOrCreate(string path, ReadOnlySpan<byte> dbKey, bool create)
    {
        EnsureProvider();
        var flags = raw.SQLITE_OPEN_READWRITE | (create ? raw.SQLITE_OPEN_CREATE : 0) | raw.SQLITE_OPEN_NOMUTEX;
        if (raw.sqlite3_open_v2(path, out var db, flags, null) != raw.SQLITE_OK)
        {
            throw new StorageIoException("Vault database could not be opened.");
        }

        try
        {
            // Raw hex key bypasses SQLCipher's internal PBKDF2 — Argon2id already ran (crypto-spec §4).
            var keyPragma = Encoding.UTF8.GetBytes($"x'{ToHex(dbKey)}'");
            Check(db, raw.sqlite3_key(db, keyPragma), "applying the database key");

            var cipherVersion = ScalarText(db, "PRAGMA cipher_version;");
            if (string.IsNullOrEmpty(cipherVersion))
            {
                // The package-name-does-not-mean-encryption guard (ADR-002).
                throw new CipherProviderException("The SQLite engine does not report SQLCipher — refusing to open a vault.");
            }

            Exec(db, "PRAGMA cipher_page_size = 4096;");
            Exec(db, "PRAGMA kdf_iter = 0;");
            Exec(db, "PRAGMA cipher_memory_security = ON;");
            Exec(db, "PRAGMA journal_mode = WAL;");
            Exec(db, "PRAGMA synchronous = FULL;");
            Exec(db, "PRAGMA temp_store = MEMORY;");
            Exec(db, "PRAGMA foreign_keys = ON;");
            return new SqlCipherConnection(db, path);
        }
        catch
        {
            raw.sqlite3_close_v2(db);
            throw;
        }
    }

    /// <summary>Runs PRAGMA integrity_check; 'ok' or throws DatabaseCorruptionException.</summary>
    public void VerifyIntegrity()
    {
        var result = ScalarText(_db ?? throw new ObjectDisposedException(nameof(SqlCipherConnection)), "PRAGMA integrity_check;");
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseCorruptionException("Vault database failed the integrity check.");
        }
    }

    public void Execute(string sql) => Exec(_db ?? throw new ObjectDisposedException(nameof(SqlCipherConnection)), sql);

    public string? ScalarText(string sql) => ScalarText(_db ?? throw new ObjectDisposedException(nameof(SqlCipherConnection)), sql);

    public byte[]? ScalarBlob(string sql)
    {
        var db = _db ?? throw new ObjectDisposedException(nameof(SqlCipherConnection));
        if (raw.sqlite3_prepare_v2(db, sql, out var stmt) != raw.SQLITE_OK)
        {
            throw new StorageIoException($"Query failed: {raw.sqlite3_errmsg(db).utf8_to_string()}");
        }

        try
        {
            return raw.sqlite3_step(stmt) switch
            {
                raw.SQLITE_ROW => ReadBlob(stmt, 0),
                raw.SQLITE_DONE => null,
                _ => throw new StorageIoException($"Query failed: {raw.sqlite3_errmsg(db).utf8_to_string()}"),
            };
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }
    }

    public SqlCipherTransaction BeginTransaction() => new(_db ?? throw new ObjectDisposedException(nameof(SqlCipherConnection)));

    /// <summary>
    /// Runs a query and materializes all rows as cell values (string, long, byte[] or null).
    /// Callers convert to their row shapes; no SQL abstraction layer is introduced.
    /// </summary>
    public IReadOnlyList<object?[]> Query(string sql)
    {
        var db = _db ?? throw new ObjectDisposedException(nameof(SqlCipherConnection));
        if (raw.sqlite3_prepare_v2(db, sql, out var stmt) != raw.SQLITE_OK)
        {
            throw new StorageIoException($"Query failed: {raw.sqlite3_errmsg(db).utf8_to_string()}");
        }

        var rows = new List<object?[]>();
        try
        {
            while (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
            {
                var columns = raw.sqlite3_column_count(stmt);
                var cells = new object?[columns];
                for (var c = 0; c < columns; c++)
                {
                    cells[c] = raw.sqlite3_column_type(stmt, c) switch
                    {
                        raw.SQLITE_INTEGER => raw.sqlite3_column_int64(stmt, c),
                        raw.SQLITE_TEXT => raw.sqlite3_column_text(stmt, c).utf8_to_string(),
                        raw.SQLITE_BLOB => ReadBlob(stmt, c),
                        _ => null,
                    };
                }

                rows.Add(cells);
            }
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }

        return rows;
    }

    /// <summary>Checkpoints and closes the WAL cleanly (call before copying/backup, on lock).</summary>
    public void Close()
    {
        if (_db is null)
        {
            return;
        }

        try
        {
            raw.sqlite3_close_v2(_db);
        }
        finally
        {
            _db = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
    }

    private static void EnsureProvider()
    {
        if (!_providerSet)
        {
            raw.SetProvider(new SQLite3Provider_e_sqlcipher());
            _providerSet = true;
        }
    }

    private static void Exec(sqlite3 db, string sql)
    {
        if (raw.sqlite3_exec(db, sql) != raw.SQLITE_OK)
        {
            throw new StorageIoException($"SQL failed: {raw.sqlite3_errmsg(db).utf8_to_string()}");
        }
    }

    private static string ScalarText(sqlite3 db, string sql)
    {
        if (raw.sqlite3_prepare_v2(db, sql, out var stmt) != raw.SQLITE_OK)
        {
            throw new StorageIoException($"Query failed: {raw.sqlite3_errmsg(db).utf8_to_string()}");
        }

        try
        {
            return raw.sqlite3_step(stmt) switch
            {
                raw.SQLITE_ROW => raw.sqlite3_column_text(stmt, 0).utf8_to_string() ?? string.Empty,
                raw.SQLITE_DONE => string.Empty,
                _ => throw new StorageIoException($"Query failed: {raw.sqlite3_errmsg(db).utf8_to_string()}"),
            };
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }
    }

    private static void Check(sqlite3 db, int rc, string what)
    {
        if (rc != raw.SQLITE_OK)
        {
            throw new CipherProviderException($"Could not {what} (code {rc}).");
        }
    }

    private static byte[]? ReadBlob(sqlite3_stmt stmt, int column)
    {
        // raw exposes the blob as a ReadOnlySpan over sqlite-owned memory; copy out immediately.
        var span = raw.sqlite3_column_blob(stmt, column);
        return span.IsEmpty ? null : span.ToArray();
    }

    internal static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}

/// <summary>Explicit transaction scope: rollback on dispose without commit (crash-safety tests).</summary>
public sealed class SqlCipherTransaction : IDisposable
{
    private readonly sqlite3 _db;
    private bool _completed;

    internal SqlCipherTransaction(sqlite3 db)
    {
        _db = db;
        if (raw.sqlite3_exec(db, "BEGIN IMMEDIATE;") != raw.SQLITE_OK)
        {
            throw new StorageIoException("Could not begin a transaction.");
        }
    }

    public void Commit()
    {
        if (raw.sqlite3_exec(_db, "COMMIT;") != raw.SQLITE_OK)
        {
            throw new StorageIoException("Could not commit the transaction.");
        }

        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed)
        {
            raw.sqlite3_exec(_db, "ROLLBACK;");
        }
    }
}
