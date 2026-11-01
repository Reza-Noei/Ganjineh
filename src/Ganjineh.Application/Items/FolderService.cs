using System.Security.Cryptography;
using System.Text;
using Ganjineh.Application.Credentials;
using Ganjineh.Application.Vaults;
using Ganjineh.Persistence.Items;
using Ganjineh.Cryptography;
using Ganjineh.Cryptography.Aead;
using Ganjineh.Cryptography.Keys;
using Ganjineh.Persistence.Organization;

namespace Ganjineh.Application.Items;

/// <summary>Hierarchical folder management (names encrypted; cycle-safe moves; transactional deletes).</summary>
public interface IFolderService
{
    Task<FolderInfo> CreateAsync(VaultSession session, string name, Guid? parentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FolderInfo>> ListAsync(VaultSession session, CancellationToken cancellationToken = default);
    Task RenameAsync(VaultSession session, Guid folderId, string newName, CancellationToken cancellationToken = default);
    Task MoveAsync(VaultSession session, Guid folderId, Guid? newParentId, CancellationToken cancellationToken = default);
    /// <summary>Deletes the folder. When deleteItems, contained live items go to Trash; otherwise they move to the root.</summary>
    Task DeleteAsync(VaultSession session, Guid folderId, bool deleteItems, CancellationToken cancellationToken = default);
    /// <summary>Live item counts per folder (for the navigation tree).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetCountsAsync(VaultSession session, CancellationToken cancellationToken = default);
}

public sealed record FolderInfo(Guid Id, Guid? ParentId, string Name, int SortOrder);

public sealed class FolderService : IFolderService
{
    private readonly ItemEncryption _encryption;

    public FolderService(ItemEncryption? encryption = null) => _encryption = encryption ?? new ItemEncryption();

    private static OrganizationRepository Repo(VaultSession session) => new(session.Database);

    public async Task<IReadOnlyDictionary<Guid, int>> GetCountsAsync(VaultSession session, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        return new OrganizationRepository(session.Database)
            .FolderItemCounts()
            .Where(pair => pair.FolderId is not null)
            .ToDictionary(pair => pair.FolderId!.Value, pair => pair.Count);
    }

    public async Task<FolderInfo> CreateAsync(VaultSession session, string name, Guid? parentId, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        AssertActive(session);
        await Task.Yield();
        var id = Guid.NewGuid();
        Repo(session).CreateFolder(id, parentId, SealName(session, id, name), 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return new FolderInfo(id, parentId, name.Trim(), 0);
    }

    public async Task<IReadOnlyList<FolderInfo>> ListAsync(VaultSession session, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        return Repo(session).ListFolders()
            .Select(f => new FolderInfo(f.Id, f.ParentId, OpenName(session, f.Id, f.NameCipher), f.SortOrder))
            .ToList();
    }

    public async Task RenameAsync(VaultSession session, Guid folderId, string newName, CancellationToken cancellationToken = default)
    {
        ValidateName(newName);
        AssertActive(session);
        await Task.Yield();
        AssertExists(session, folderId);
        Repo(session).RenameFolder(folderId, SealName(session, folderId, newName));
    }

    public async Task MoveAsync(VaultSession session, Guid folderId, Guid? newParentId, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        var repository = Repo(session);
        AssertExists(session, folderId);
        if (newParentId is { } parent)
        {
            AssertExists(session, parent);
            if (IsDescendant(repository, folderId, parent))
            {
                throw new ValidationException("FolderCircularMove");
            }
        }

        repository.MoveFolder(folderId, newParentId);
    }

    public async Task DeleteAsync(VaultSession session, Guid folderId, bool deleteItems, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        var repository = Repo(session);
        AssertExists(session, folderId);

        if (deleteItems)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var row in new ItemRepository(session.Database)
                         .ListAllLive()
                         .Where(r => r.FolderId == folderId))
            {
                new ItemRepository(session.Database).Trash(row.Id, now);
            }
        }

        // Items fall back to the root; children of the deleted folder are re-parented to its
        // parent so the tree stays connected.
        var folder = repository.GetFolder(folderId);
        var grandparentId = folder?.ParentId;
        foreach (var child in repository.ListFolders().Where(f => f.ParentId == folderId))
        {
            repository.MoveFolder(child.Id, grandparentId);
        }

        repository.DeleteFolder(folderId);
    }

    internal static bool IsDescendant(OrganizationRepository repository, Guid ancestorId, Guid candidateId)
    {
        var current = candidateId;
        var hops = 0;
        while (hops++ < 100) // hard bound: cyclic graphs cannot occur (moves are cycle-checked)
        {
            var folder = repository.GetFolder(current);
            if (folder is null)
            {
                return false;
            }

            if (folder.ParentId == ancestorId)
            {
                return true;
            }

            current = folder.ParentId ?? Guid.Empty;
            if (current == Guid.Empty)
            {
                return false;
            }
        }

        return true;
    }

    private static void AssertExists(VaultSession session, Guid folderId)
    {
        if (Repo(session).GetFolder(folderId) is null)
        {
            throw new KeyNotFoundException("Folder not found.");
        }
    }

    private byte[] SealName(VaultSession session, Guid folderId, string name) =>
        _encryption.EncryptMeta(
            session.Keyring, session.Header.VaultId, FolderRecordId(session, folderId),
            Encoding.UTF8.GetBytes(name.Trim()));

    private string OpenName(VaultSession session, Guid folderId, byte[] envelope) =>
        Encoding.UTF8.GetString(_encryption.DecryptMeta(
            session.Keyring, session.Header.VaultId, FolderRecordId(session, folderId), envelope));

    // AAD binds each folder name envelope to its own folder ID — swapping names between folders
    // fails authentication.
    private static string FolderRecordId(VaultSession session, Guid folderId) =>
        "folder:" + folderId.ToString();

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("FolderNameRequired");
        }

        if (name.Trim().Length > 100)
        {
            throw new ValidationException("FolderNameTooLong");
        }
    }

    private static void AssertActive(VaultSession session)
    {
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Folder operations require an unlocked vault session.");
        }
    }
}
