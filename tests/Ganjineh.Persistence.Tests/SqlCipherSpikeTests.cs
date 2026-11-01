using System.Text;
using SQLitePCL;
using Xunit;
using Xunit.Abstractions;

namespace Ganjineh.Persistence.Tests;

/// <summary>Q1 spike: verify the raw e_sqlcipher provider encrypts for real on this platform.</summary>
public class SqlCipherSpikeTests
{
    private const int OpenReadWriteCreate = raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE;

    private static bool _providerSet;

    private readonly ITestOutputHelper _output;

    public SqlCipherSpikeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SqlCipher_Encrypts_RoundTrip_On_This_Platform()
    {
        EnsureProvider();
        var path = Path.Combine(Path.GetTempPath(), $"ganjineh-spike-{Guid.NewGuid():N}.db");
        var keyPragma = $"x'{new string('A', 64)}'"; // raw 32-byte key in SQLCipher hex notation
        try
        {
            Assert.Equal(raw.SQLITE_OK, raw.sqlite3_open_v2(path, out var db, OpenReadWriteCreate, null));
            try
            {
                Key(db, keyPragma);

                var cipherVersion = ExecScalar(db, "PRAGMA cipher_version;");
                Assert.False(string.IsNullOrEmpty(cipherVersion), "cipher_version empty — this is NOT SQLCipher");
                _output.WriteLine($"cipher_version: {cipherVersion}");

                Exec(db, "PRAGMA cipher_page_size = 4096; PRAGMA kdf_iter = 0; PRAGMA journal_mode = WAL;");
                Exec(db, "CREATE TABLE t (v TEXT);");
                Exec(db, "INSERT INTO t VALUES ('سری sentinel 🔒');");
            }
            finally
            {
                raw.sqlite3_close_v2(db);
            }

            // File must not be an ordinary SQLite database at rest.
            var header = new byte[16];
            using (var fs = File.OpenRead(path))
            {
                fs.ReadExactly(header);
            }

            Assert.False(
                Encoding.ASCII.GetString(header).StartsWith("SQLite format 3", StringComparison.Ordinal),
                "database file has plaintext SQLite header — encryption is OFF");

            // Reopen with the correct raw key and read the sentinel back.
            Assert.Equal(raw.SQLITE_OK, raw.sqlite3_open_v2(path, out var good, raw.SQLITE_OPEN_READWRITE, null));
            try
            {
                Key(good, keyPragma);
                Assert.Contains("سری", ExecScalar(good, "SELECT v FROM t;"));
            }
            finally
            {
                raw.sqlite3_close_v2(good);
            }

            // A wrong raw key must not yield readable data: prepare/step must fail (SQLITE_NOTADB
            // family), never return a row.
            Assert.Equal(raw.SQLITE_OK, raw.sqlite3_open_v2(path, out var bad, raw.SQLITE_OPEN_READWRITE, null));
            try
            {
                Key(bad, $"x'{new string('B', 64)}'");
                Assert.NotEqual(raw.SQLITE_ROW, StepRc(bad, "SELECT count(*) FROM t;"));
            }
            finally
            {
                raw.sqlite3_close_v2(bad);
            }
        }
        finally
        {
            try
            {
                File.Delete(path);
                File.Delete(path + "-wal");
                File.Delete(path + "-shm");
            }
            catch (IOException)
            {
            }
        }
    }

    private static void EnsureProvider()
    {
        if (!_providerSet)
        {
            raw.SetProvider(new SQLite3Provider_e_sqlcipher());
            _providerSet = true;
        }
    }

    private static void Key(sqlite3 db, string keyPragma)
    {
        Assert.Equal(raw.SQLITE_OK, raw.sqlite3_key(db, Encoding.UTF8.GetBytes(keyPragma)));
    }

    private static void Exec(sqlite3 db, string sql)
    {
        if (raw.sqlite3_exec(db, sql) != raw.SQLITE_OK)
        {
            throw new InvalidOperationException($"sqlite exec failed: {raw.sqlite3_errmsg(db).utf8_to_string()}");
        }
    }

    private static int StepRc(sqlite3 db, string sql)
    {
        if (raw.sqlite3_prepare_v2(db, sql, out var stmt) != raw.SQLITE_OK)
        {
            return -1;
        }

        try
        {
            return raw.sqlite3_step(stmt);
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }
    }

    private static string ExecScalar(sqlite3 db, string sql)
    {
        Assert.Equal(raw.SQLITE_OK, raw.sqlite3_prepare_v2(db, sql, out var stmt));
        try
        {
            var rc = raw.sqlite3_step(stmt);
            if (rc != raw.SQLITE_ROW && rc != raw.SQLITE_DONE)
            {
                throw new InvalidOperationException($"sqlite step failed rc={rc}: {raw.sqlite3_errmsg(db).utf8_to_string()}");
            }

            return rc == raw.SQLITE_ROW
                ? raw.sqlite3_column_text(stmt, 0).utf8_to_string()
                : string.Empty;
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }
    }
}
