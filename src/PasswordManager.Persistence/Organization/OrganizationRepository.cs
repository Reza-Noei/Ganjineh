using System.Buffers.Binary;

namespace PasswordManager.Persistence.Organization;

/// <summary>Encrypted folder row (name is an envelope; hierarchy is structural).</summary>
public sealed record FolderRow(Guid Id, Guid? ParentId, byte[] NameCipher, int SortOrder, long CreatedAt);

/// <summary>Encrypted tag row.</summary>
public sealed record TagRow(Guid Id, byte[] NameCipher, long CreatedAt);

/// <summary>Attachment metadata row (content lives in chunked .pmblob files).</summary>
public sealed record AttachmentRow(
    Guid Id,
    Guid ItemId,
    byte[] FilenameCipher,
    long SizeBytes,
    byte[] FileKeyCipher,
    long CreatedAt);

/// <summary>
/// Key-free organization + attachment-meta persistence: names and filenames arrive as sealed
/// envelopes from the Application layer; this class never sees plaintext or keys.
/// </summary>
public sealed class OrganizationRepository
{
    private readonly SqlCipher.VaultDatabase _database;

    public OrganizationRepository(SqlCipher.VaultDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    // ── Folders ──

    public void CreateFolder(Guid id, Guid? parentId, byte[] nameCipher, int sortOrder, long nowUnix)
    {
        var parent = parentId is null ? "NULL" : $"'{parentId}'";
        var hex = SqlCipher.SqlCipherConnection.ToHex(nameCipher);
        _database.Connection.Execute(
            $"INSERT INTO folders (id, parent_id, name_cipher, sort_order, created_at) VALUES ('{id}', {parent}, x'{hex}', {sortOrder}, {nowUnix});");
    }

    public void RenameFolder(Guid id, byte[] nameCipher)
    {
        var hex = SqlCipher.SqlCipherConnection.ToHex(nameCipher);
        _database.Connection.Execute($"UPDATE folders SET name_cipher = x'{hex}' WHERE id = '{id}';");
    }

    public void MoveFolder(Guid id, Guid? newParentId)
    {
        var parent = newParentId is null ? "NULL" : $"'{newParentId}'";
        _database.Connection.Execute($"UPDATE folders SET parent_id = {parent} WHERE id = '{id}';");
    }

    /// <summary>Deletes the folder; contained items fall back to the root (folder_id = NULL) in one
    /// transaction. Item deletion is the caller's explicit choice (never implicit here).</summary>
    public void DeleteFolder(Guid id)
    {
        using var tx = _database.Connection.BeginTransaction();
        _database.Connection.Execute($"UPDATE items SET folder_id = NULL WHERE folder_id = '{id}';");
        _database.Connection.Execute($"DELETE FROM folders WHERE id = '{id}';");
        tx.Commit();
    }

    public FolderRow? GetFolder(Guid id)
    {
        var rows = QueryFolders($"SELECT id, parent_id, name_cipher, sort_order, created_at FROM folders WHERE id = '{id}';");
        return rows.Count > 0 ? rows[0] : null;
    }

    public IReadOnlyList<FolderRow> ListFolders() =>
        QueryFolders("SELECT id, parent_id, name_cipher, sort_order, created_at FROM folders ORDER BY sort_order, created_at;");

    private IReadOnlyList<FolderRow> QueryFolders(string sql)
    {
        var rows = new List<FolderRow>();
        foreach (var cells in _database.Connection.Query(sql))
        {
            rows.Add(new FolderRow(
                Id: new Guid((string)cells[0]!),
                ParentId: cells[1] is string parent ? new Guid(parent) : null,
                NameCipher: (byte[])cells[2]!,
                SortOrder: (int)(long)cells[3]!,
                CreatedAt: (long)cells[4]!));
        }

        return rows;
    }

    // ── Tags ──

    public void CreateTag(Guid id, byte[] nameCipher, long nowUnix)
    {
        var hex = SqlCipher.SqlCipherConnection.ToHex(nameCipher);
        _database.Connection.Execute(
            $"INSERT INTO tags (id, name_cipher, created_at) VALUES ('{id}', x'{hex}', {nowUnix});");
    }

    public void RenameTag(Guid id, byte[] nameCipher)
    {
        var hex = SqlCipher.SqlCipherConnection.ToHex(nameCipher);
        _database.Connection.Execute($"UPDATE tags SET name_cipher = x'{hex}' WHERE id = '{id}';");
    }

    public void DeleteTag(Guid id) =>
        _database.Connection.Execute($"DELETE FROM tags WHERE id = '{id}';");

    public IReadOnlyList<TagRow> ListTags() =>
        _database.Connection.Query("SELECT id, name_cipher, created_at FROM tags ORDER BY created_at;")
            .Select(r => new TagRow(new Guid((string)r[0]!), (byte[])r[1]!, (long)r[2]!))
            .ToList();

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

    // ── Attachments ──

    public void AddAttachment(AttachmentRow row)
    {
        var hexName = SqlCipher.SqlCipherConnection.ToHex(row.FilenameCipher);
        var hexKey = SqlCipher.SqlCipherConnection.ToHex(row.FileKeyCipher);
        _database.Connection.Execute(
            $"INSERT INTO attachments (id, item_id, filename_cipher, size_bytes, filekey_cipher, created_at) " +
            $"VALUES ('{row.Id}', '{row.ItemId}', x'{hexName}', {row.SizeBytes}, x'{hexKey}', {row.CreatedAt});");
    }

    public IReadOnlyList<AttachmentRow> ListAttachments(Guid itemId) =>
        _database.Connection.Query(
                $"SELECT id, item_id, filename_cipher, size_bytes, filekey_cipher, created_at FROM attachments WHERE item_id = '{itemId}' ORDER BY created_at;")
            .Select(MapAttachment)
            .ToList();

    public AttachmentRow? GetAttachment(Guid id)
    {
        var rows = _database.Connection.Query(
            $"SELECT id, item_id, filename_cipher, size_bytes, filekey_cipher, created_at FROM attachments WHERE id = '{id}';");
        return rows.Count > 0 ? MapAttachment(rows[0]) : null;
    }

    public void DeleteAttachment(Guid id) =>
        _database.Connection.Execute($"DELETE FROM attachments WHERE id = '{id}';");

    public IReadOnlyList<Guid> ListAllAttachmentIds() =>
        _database.Connection.Query("SELECT id FROM attachments;")
            .Select(r => new Guid((string)r[0]!))
            .ToList();

    private static AttachmentRow MapAttachment(object?[] cells) => new(
        Id: new Guid((string)cells[0]!),
        ItemId: new Guid((string)cells[1]!),
        FilenameCipher: (byte[])cells[2]!,
        SizeBytes: (long)cells[3]!,
        FileKeyCipher: (byte[])cells[4]!,
        CreatedAt: (long)cells[5]!);
}
