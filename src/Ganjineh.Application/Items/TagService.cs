using System.Security.Cryptography;
using System.Text;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Vaults;
using PasswordManager.Persistence.Organization;

namespace PasswordManager.Application.Items;

/// <summary>Tag management (encrypted names; duplicate prevention by normalized comparison).</summary>
public interface ITagService
{
    Task<TagInfo> CreateAsync(VaultSession session, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TagInfo>> ListAsync(VaultSession session, CancellationToken cancellationToken = default);
    Task RenameAsync(VaultSession session, Guid tagId, string newName, CancellationToken cancellationToken = default);
    Task DeleteAsync(VaultSession session, Guid tagId, CancellationToken cancellationToken = default);
}

public sealed record TagInfo(Guid Id, string Name, int UsageCount);

public sealed class TagService : ITagService
{
    private readonly ItemEncryption _encryption;

    public TagService(ItemEncryption? encryption = null) => _encryption = encryption ?? new ItemEncryption();

    private static OrganizationRepository Repo(VaultSession session) => new(session.Database);

    public async Task<TagInfo> CreateAsync(VaultSession session, string name, CancellationToken cancellationToken = default)
    {
        var normalized = ValidateAndNormalize(name);
        AssertActive(session);
        await Task.Yield();

        // Duplicate policy (folders-and-tags.md): tags whose display names are equal after
        // trim + invariant lowercase + Persian normalization are the same tag.
        var existing = await ListAsync(session, cancellationToken);
        var duplicate = existing.FirstOrDefault(t =>
            string.Equals(TagNormalizer.Normalize(t.Name), TagNormalizer.Normalize(name), StringComparison.Ordinal));
        if (duplicate is not null)
        {
            return duplicate;
        }

        var id = Guid.NewGuid();
        Repo(session).CreateTag(id, SealName(session, id, name), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return new TagInfo(id, name.Trim(), 0);
    }

    public async Task<IReadOnlyList<TagInfo>> ListAsync(VaultSession session, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        var repository = Repo(session);
        var counts = repository.TagUsageCounts().ToDictionary(c => c.TagId, c => c.Count);
        return repository.ListTags()
            .Select(t => new TagInfo(t.Id, OpenName(session, t.Id, t.NameCipher), counts.GetValueOrDefault(t.Id)))
            .ToList();
    }

    public async Task RenameAsync(VaultSession session, Guid tagId, string newName, CancellationToken cancellationToken = default)
    {
        var normalized = ValidateAndNormalize(newName);
        AssertActive(session);
        await Task.Yield();
        var repository = Repo(session);

        var existing = await ListAsync(session, cancellationToken);
        if (existing.Any(t => t.Id != tagId
            && string.Equals(TagNormalizer.Normalize(t.Name), normalized, StringComparison.Ordinal)))
        {
            throw new ValidationException("TagDuplicate");
        }

        repository.RenameTag(tagId, SealName(session, tagId, newName));
    }

    public async Task DeleteAsync(VaultSession session, Guid tagId, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        // item_tags rows cascade away with the tag (FK ON DELETE CASCADE).
        Repo(session).DeleteTag(tagId);
    }

    private static string ValidateAndNormalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("TagNameRequired");
        }

        if (name.Trim().Length > 60)
        {
            throw new ValidationException("TagNameTooLong");
        }

        return TagNormalizer.Normalize(name);
    }

    private byte[] SealName(VaultSession session, Guid tagId, string name) =>
        _encryption.EncryptMeta(
            session.Keyring, session.Header.VaultId, "tag:" + tagId.ToString(),
            Encoding.UTF8.GetBytes(name.Trim()));

    private string OpenName(VaultSession session, Guid tagId, byte[] envelope) =>
        Encoding.UTF8.GetString(_encryption.DecryptMeta(
            session.Keyring, session.Header.VaultId, "tag:" + tagId.ToString(), envelope));

    private static void AssertActive(VaultSession session)
    {
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Tag operations require an unlocked vault session.");
        }
    }
}

/// <summary>Tag display-name normalization for duplicate detection (documented policy).</summary>
public static class TagNormalizer
{
    public static string Normalize(string name) =>
        SearchNormalizer.Normalize(name.Trim()).Replace(' ', '-');
}
