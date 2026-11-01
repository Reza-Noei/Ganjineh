using Ganjineh.Persistence.SqlCipher;

namespace Ganjineh.Persistence.Migrations;

/// <summary>
/// Forward-only schema migrations (database-design.md §6).
/// v1 = Phase 3 baseline (items + item_versions; legacy databases predate the migrations table).
/// v2 = folders, tags, item_tags, attachments, items.folder_id.
/// Every migration runs inside one transaction; a failure rolls back completely (tested).
/// </summary>
public static class SchemaMigrator
{
    public const int CurrentVersion = 2;

    public static void MigrateToCurrent(VaultDatabase database)
    {
        var connection = database.Connection;
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
              version     INTEGER PRIMARY KEY,
              applied_at  INTEGER NOT NULL
            );
            """);

        var applied = MaxAppliedVersion(connection);
        if (applied >= CurrentVersion)
        {
            return;
        }

        using var tx = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (applied < 1)
        {
            // v1 — Phase 3 baseline tables (idempotent for legacy DBs that already have them).
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS items (
                  id          TEXT PRIMARY KEY,
                  type        INTEGER NOT NULL,
                  favorite    INTEGER NOT NULL DEFAULT 0,
                  trashed_at  INTEGER NULL,
                  created_at  INTEGER NOT NULL,
                  updated_at  INTEGER NOT NULL
                );
                """);
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS item_versions (
                  item_id        TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                  version        INTEGER NOT NULL,
                  payload_cipher BLOB NOT NULL,
                  created_at     INTEGER NOT NULL,
                  PRIMARY KEY (item_id, version)
                );
                """);
            connection.Execute("INSERT OR IGNORE INTO schema_migrations (version, applied_at) VALUES (1, " + now + ");");
        }

        if (applied < 2)
        {
            // v2 — organization + attachments.
            connection.Execute("ALTER TABLE items ADD COLUMN folder_id TEXT NULL REFERENCES folders(id);");
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS folders (
                  id          TEXT PRIMARY KEY,
                  parent_id   TEXT NULL REFERENCES folders(id),
                  name_cipher BLOB NOT NULL,
                  sort_order  INTEGER NOT NULL DEFAULT 0,
                  created_at  INTEGER NOT NULL
                );
                """);
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS tags (
                  id          TEXT PRIMARY KEY,
                  name_cipher BLOB NOT NULL,
                  created_at  INTEGER NOT NULL
                );
                """);
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS item_tags (
                  item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                  tag_id  TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
                  PRIMARY KEY (item_id, tag_id)
                );
                """);
            connection.Execute("""
                CREATE TABLE IF NOT EXISTS attachments (
                  id              TEXT PRIMARY KEY,
                  item_id         TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                  filename_cipher BLOB NOT NULL,
                  size_bytes      INTEGER NOT NULL,
                  filekey_cipher  BLOB NOT NULL,
                  created_at      INTEGER NOT NULL
                );
                """);
            connection.Execute("CREATE INDEX IF NOT EXISTS ix_items_folder ON items(folder_id);");
            connection.Execute("CREATE INDEX IF NOT EXISTS ix_att_item ON attachments(item_id);");
            connection.Execute("INSERT OR REPLACE INTO schema_migrations (version, applied_at) VALUES (2, " + now + ");");
        }

        tx.Commit();
    }

    private static long MaxAppliedVersion(SqlCipherConnection connection)
    {
        var value = connection.ScalarText("SELECT COALESCE(MAX(version), 0) FROM schema_migrations;");
        return long.TryParse(value, out var parsed) ? parsed : 0;
    }
}
