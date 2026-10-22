using System.Buffers.Binary;

namespace PasswordManager.Persistence.Items;

/// <summary>
/// Item-type identifiers (database-design.md §3). Phase 3 implements Login only; the column and
/// payload-envelope format are type-agnostic so later types need no rewrite.
/// </summary>
public enum ItemType : int
{
    Login = 1,
    SecureNote = 2,
    PaymentCard = 3,
    Identity = 4,
    Wifi = 5,
    ApiCredential = 6,
    SoftwareLicense = 7,
    RecoveryCodes = 8,
  Custom = 9,
}

/// <summary>Structural row of an item (non-sensitive columns; payload is an envelope).</summary>
public sealed record ItemRow(
    Guid Id,
    int Type,
    bool Favorite,
    long? TrashedAt,
    long CreatedAt,
    long UpdatedAt,
    Guid? FolderId = null);

/// <summary>
/// Key-free item persistence: the repository stores/returns envelope blobs only — encryption and
/// decryption happen in the Application layer with the session's item key (architecture.md §1:
/// Persistence never sees plaintext secrets or keys).
/// </summary>
public sealed class ItemRepository
{
    private readonly SqlCipher.VaultDatabase _database;

    public ItemRepository(SqlCipher.VaultDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        _database.Connection.Execute("""
            CREATE TABLE IF NOT EXISTS items (
              id          TEXT PRIMARY KEY,
              type        INTEGER NOT NULL,
              favorite    INTEGER NOT NULL DEFAULT 0,
              trashed_at  INTEGER NULL,
              created_at  INTEGER NOT NULL,
              updated_at  INTEGER NOT NULL
            );
            """);
        _database.Connection.Execute("""
            CREATE TABLE IF NOT EXISTS item_versions (
              item_id        TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
              version        INTEGER NOT NULL,
              payload_cipher BLOB NOT NULL,
              created_at     INTEGER NOT NULL,
              PRIMARY KEY (item_id, version)
            );
            """);
        _database.Connection.Execute("CREATE INDEX IF NOT EXISTS ix_items_type ON items(type) WHERE trashed_at IS NULL;");
    }

    /// <summary>Inserts an item plus its first version in one transaction.</summary>
    public Guid Insert(Guid id, int type, byte[] payloadCipher, long nowUnix)
    {
        using var tx = _database.Connection.BeginTransaction();
        _database.Connection.Execute(
            $"INSERT INTO items (id, type, favorite, trashed_at, created_at, updated_at) " +
            $"VALUES ('{id}', {type}, 0, NULL, {nowUnix}, {nowUnix});");
        InsertVersion(id, 1, payloadCipher, nowUnix);
        tx.Commit();
        return id;
    }

    /// <summary>Appends an updated payload version and bumps updated_at in one transaction.</summary>
    public void Update(Guid id, byte[] payloadCipher, long nowUnix)
    {
        var current = GetRow(id) ?? throw new InvalidOperationException("Item not found.");
        using var tx = _database.Connection.BeginTransaction();
        var nextVersion = CurrentVersion(id) + 1;
        InsertVersion(id, nextVersion, payloadCipher, nowUnix);
        _database.Connection.Execute($"UPDATE items SET updated_at = {nowUnix} WHERE id = '{id}';");
        tx.Commit();
    }

    public void SetFavorite(Guid id, bool favorite, long nowUnix)
    {
        var value = favorite ? 1 : 0;
        _database.Connection.Execute($"UPDATE items SET favorite = {value}, updated_at = {nowUnix} WHERE id = '{id}';");
    }

    /// <summary>Permanent delete (trash restore path). Cascades to versions and tags.</summary>
    public void Delete(Guid id) =>
        _database.Connection.Execute($"DELETE FROM items WHERE id = '{id}';");

    public ItemRow? GetRow(Guid id) => QueryRow($"SELECT id, type, favorite, trashed_at, created_at, updated_at, folder_id FROM items WHERE id = '{id}';");

    public IReadOnlyList<ItemRow> ListRows(int type) =>
        QueryRows($"SELECT id, type, favorite, trashed_at, created_at, updated_at, folder_id FROM items WHERE type = {type} AND trashed_at IS NULL ORDER BY updated_at DESC;");

    public IReadOnlyList<ItemRow> ListAllLive() =>
        QueryRows("SELECT id, type, favorite, trashed_at, created_at, updated_at, folder_id FROM items WHERE trashed_at IS NULL ORDER BY updated_at DESC;");

    public IReadOnlyList<ItemRow> ListTrashed() =>
        QueryRows("SELECT id, type, favorite, trashed_at, created_at, updated_at, folder_id FROM items WHERE trashed_at IS NOT NULL ORDER BY trashed_at DESC;");

    public void Trash(Guid id, long trashedAt) =>
        _database.Connection.Execute($"UPDATE items SET trashed_at = {trashedAt} WHERE id = '{id}';");

    public void Restore(Guid id)
    {
        var row = GetRow(id) ?? throw new InvalidOperationException("Item not found.");
        _database.Connection.Execute($"UPDATE items SET trashed_at = NULL, updated_at = {row.UpdatedAt} WHERE id = '{id}';");
    }

    /// <summary>Permanently removes items soft-deleted before the cutoff. Returns affected ids.</summary>
    public IReadOnlyList<Guid> PurgeTrashedBefore(long cutoffUnix)
    {
        var rows = QueryRows($"SELECT id, type, favorite, trashed_at, created_at, updated_at, folder_id FROM items WHERE trashed_at IS NOT NULL AND trashed_at < {cutoffUnix};");
        var ids = rows.Select(r => r.Id).ToList();
        foreach (var id in ids)
        {
            Delete(id);
        }

        return ids;
    }

    public void SetFolder(Guid id, Guid? folderId)
    {
        var value = folderId is null ? "NULL" : $"'{folderId}'";
        _database.Connection.Execute($"UPDATE items SET folder_id = {value} WHERE id = '{id}';");
    }

    public IReadOnlyList<Guid> GetTagIds(Guid id) =>
        _database.Connection.Query($"SELECT tag_id FROM item_tags WHERE item_id = '{id}';")
            .Select(r => new Guid((string)r[0]!))
            .ToList();

    public void SetTags(Guid id, IReadOnlyCollection<Guid> tagIds)
    {
        using var tx = _database.Connection.BeginTransaction();
        _database.Connection.Execute($"DELETE FROM item_tags WHERE item_id = '{id}';");
        foreach (var tagId in tagIds.Distinct())
        {
            _database.Connection.Execute($"INSERT OR IGNORE INTO item_tags (item_id, tag_id) VALUES ('{id}', '{tagId}');");
        }

        tx.Commit();
    }

    public IReadOnlyList<(Guid TagId, int Count)> TagUsageCounts() =>
        _database.Connection.Query(
                "SELECT t.id, COUNT(it.item_id) FROM tags t LEFT JOIN item_tags it ON it.tag_id = t.id GROUP BY t.id;")
            .Select(r => (new Guid((string)r[0]!), (int)(long)r[1]!))
            .ToList();

    public IReadOnlyList<(Guid? FolderId, int Count)> FolderItemCounts() =>
        _database.Connection.Query(
                "SELECT folder_id, COUNT(*) FROM items WHERE trashed_at IS NULL GROUP BY folder_id;")
            .Select(r => ((Guid?)r[0] is null ? (Guid?)null : new Guid((string)r[0]!), (int)(long)r[1]!))
            .ToList();

    public byte[]? GetCurrentPayload(Guid id) =>
        _database.Connection.ScalarBlob(
            $"SELECT payload_cipher FROM item_versions WHERE item_id = '{id}' ORDER BY version DESC;");

    public byte[]? GetVersionPayload(Guid id, int version) =>
        _database.Connection.ScalarBlob(
            $"SELECT payload_cipher FROM item_versions WHERE item_id = '{id}' AND version = {version};");

    public IReadOnlyList<(int Version, long CreatedAt)> ListVersions(Guid id) =>
        _database.Connection.Query(
                $"SELECT version, created_at FROM item_versions WHERE item_id = '{id}' ORDER BY version;")
            .Select(r => ((int)(long)r[0]!, (long)r[1]!))
            .ToList();

    public void DeleteVersion(Guid id, int version) =>
        _database.Connection.Execute($"DELETE FROM item_versions WHERE item_id = '{id}' AND version = {version};");

    private void InsertVersion(Guid id, int version, byte[] payloadCipher, long nowUnix)
    {
        var hex = SqlCipher.SqlCipherConnection.ToHex(payloadCipher);
        _database.Connection.Execute(
            $"INSERT INTO item_versions (item_id, version, payload_cipher, created_at) VALUES ('{id}', {version}, x'{hex}', {nowUnix});");
    }

    private int CurrentVersion(Guid id)
    {
        var value = _database.Connection.ScalarText($"SELECT COALESCE(MAX(version), 0) FROM item_versions WHERE item_id = '{id}';");
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private ItemRow? QueryRow(string sql)
    {
        var rows = QueryRows(sql);
        return rows.Count > 0 ? rows[0] : null;
    }

    private IReadOnlyList<ItemRow> QueryRows(string sql)
    {
        var rows = new List<ItemRow>();
        foreach (var cells in _database.Connection.Query(sql))
        {
            rows.Add(new ItemRow(
                Id: new Guid((string)cells[0]!),
                Type: checked((int)(long)cells[1]!),
                Favorite: (long)cells[2]! != 0,
                TrashedAt: (long?)cells[3],
                CreatedAt: (long)cells[4]!,
                UpdatedAt: (long)cells[5]!,
                FolderId: cells.Length > 6 && cells[6] is string folderId ? new Guid(folderId) : null));
        }

        return rows;
    }
}
