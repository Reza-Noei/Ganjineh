using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PasswordManager.Cryptography;
using PasswordManager.Cryptography.Aead;
using PasswordManager.Cryptography.Errors;
using PasswordManager.Cryptography.Keys;
using PasswordManager.Persistence;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Vaults;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.Organization;

namespace PasswordManager.Application.Items;

/// <summary>
/// Unified vault-item use cases: CRUD across all item kinds, organization (folders/tags),
/// favorites, history and trash — all bound to the active session and key-free below this layer.
/// </summary>
public interface IVaultItemService
{
    Task<ItemSummary> CreateAsync(VaultSession session, ItemDraft draft, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ItemSummary>> ListAsync(VaultSession session, ItemFilter? filter = null, CancellationToken cancellationToken = default);
    Task<ItemDetail> GetAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);
    Task<ItemSummary> UpdateAsync(VaultSession session, Guid id, ItemDraft draft, CancellationToken cancellationToken = default);
    Task ToggleFavoriteAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);

    Task TrashAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);
    Task RestoreAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);
    Task PurgeAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);
    Task<int> EmptyTrashAsync(VaultSession session, int retentionDays, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ItemVersionInfo>> GetHistoryAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);
    Task<ItemSummary> RestoreVersionAsync(VaultSession session, Guid id, int version, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CredentialSearchEntry>> BuildSearchIndexAsync(VaultSession session, ItemFilter? filter = null, CancellationToken cancellationToken = default);
}

public sealed record ItemDraft
{
    public required ItemType Kind { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object?>? Data { get; init; }
    public List<CustomField> CustomFields { get; init; } = [];
    public Guid? FolderId { get; init; }
    public List<Guid> TagIds { get; init; } = [];
}

public sealed record ItemSummary(
    Guid Id,
    ItemType Kind,
    string Title,
    string? Subtitle,
    bool Favorite,
    Guid? FolderId,
    IReadOnlyList<Guid> TagIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ItemDetail(
    Guid Id,
    ItemType Kind,
    string Title,
    string? Description,
    Dictionary<string, object?> Data,
    List<CustomField> CustomFields,
    bool Favorite,
    Guid? FolderId,
    List<Guid> TagIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ItemFilter(
    ItemType? Kind = null,
    Guid? FolderId = null,
    IReadOnlyList<Guid>? TagIds = null,
    bool FavoritesOnly = false,
    bool TrashedOnly = false,
    DateTimeOffset? UpdatedAfter = null,
    DateTimeOffset? UpdatedBefore = null,
    string? SortBy = null);

public sealed record ItemVersionInfo(int Version, DateTimeOffset CreatedAt);

public sealed class VaultItemService : IVaultItemService
{
    private readonly ItemEncryption _encryption;
    private readonly IAuthenticatedEncryptionService _metaEncryption = new AesGcmAuthenticatedEncryption();
    private readonly int _historyRetentionPerItem;

    public VaultItemService(ItemEncryption? encryption = null, int historyRetentionPerItem = 50)
    {
        _encryption = encryption ?? new ItemEncryption();
        _historyRetentionPerItem = historyRetentionPerItem;
    }

    private static ItemRepository Repo(VaultSession session) => new(session.Database);
    private static OrganizationRepository OrgRepo(VaultSession session) => new(session.Database);

    public async Task<ItemSummary> CreateAsync(VaultSession session, ItemDraft draft, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = Guid.NewGuid();
        var payload = ToPayload(draft);
        var envelope = _encryption.EncryptPayload(sessionLocal.Keyring, sessionLocal.Header.VaultId, id, payload);
        repository.Insert(id, (int)draft.Kind, envelope, now);
        ApplyOrganization(sessionLocal, repository, id, draft, keepTagsWhenNull: false);
        return await GetSummaryAsync(sessionLocal, repository, id, cancellationToken);
    }

    public async Task<IReadOnlyList<ItemSummary>> ListAsync(VaultSession session, ItemFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        var rows = SelectRows(repository, filter);
        var summaries = new List<ItemSummary>();
        foreach (var row in rows)
        {
            summaries.Add(await GetSummaryAsync(sessionLocal, repository, row.Id, cancellationToken));
        }

        return ApplySort(summaries, filter?.SortBy);
    }

    public async Task<ItemDetail> GetAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        var row = repository.GetRow(id) ?? throw new KeyNotFoundException("Item not found.");
        var envelope = repository.GetCurrentPayload(id)
            ?? throw new DatabaseCorruptionException("Item payload missing.");
        var payload = ParsePayload(envelope, sessionLocal, id);
        var (data, fields) = SplitPayload(payload);
        NormalizeData(data);
        return new ItemDetail(
            id, (ItemType)row.Type, payload.Title, payload.Description,
            data, fields, row.Favorite, row.FolderId,
            repository.GetTagIds(id).ToList(),
            DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt),
            DateTimeOffset.FromUnixTimeSeconds(row.UpdatedAt));
    }

    public async Task<ItemSummary> UpdateAsync(VaultSession session, Guid id, ItemDraft draft, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        var row = repository.GetRow(id) ?? throw new KeyNotFoundException("Item not found.");
        if (row.TrashedAt is not null)
        {
            throw new ValidationException("ItemTrashed");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = ToPayload(draft);
        var envelope = _encryption.EncryptPayload(sessionLocal.Keyring, sessionLocal.Header.VaultId, id, payload);
        repository.Update(id, envelope, now);
        repository.SetFolder(id, draft.FolderId);
        ApplyTags(sessionLocal, repository, id, draft.TagIds, keepTagsWhenNull: false);
        await PruneHistoryAsync(repository, id);
        return await GetSummaryAsync(sessionLocal, repository, id, cancellationToken);
    }

    public async Task ToggleFavoriteAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        var row = repository.GetRow(id) ?? throw new KeyNotFoundException("Item not found.");
        repository.SetFavorite(id, !row.Favorite, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public async Task TrashAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        repository.Trash(id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public async Task RestoreAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        repository.Restore(id);
    }

    public async Task PurgeAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        new OrganizationRepository(sessionLocal.Database)
            .ListAttachments(id)
            .ToList()
            .ForEach(a => DeleteAttachmentFile(sessionLocal, a.Id));
        repository.Delete(id);
    }

    public async Task<int> EmptyTrashAsync(VaultSession session, int retentionDays, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        var cutoff = DateTimeOffset.UtcNow
            .AddDays(-Math.Clamp(retentionDays, 0, 3650))
            .ToUnixTimeSeconds();
        var purged = repository.PurgeTrashedBefore(cutoff);
        foreach (var id in purged)
        {
            foreach (var attachment in new OrganizationRepository(sessionLocal.Database).ListAttachments(id))
            {
                DeleteAttachmentFile(sessionLocal, attachment.Id);
            }
        }

        return purged.Count;
    }

    public async Task<IReadOnlyList<ItemVersionInfo>> GetHistoryAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        return repository.ListVersions(id)
            .Select(v => new ItemVersionInfo(v.Version, DateTimeOffset.FromUnixTimeSeconds(v.CreatedAt)))
            .OrderByDescending(v => v.Version)
            .ToList();
    }

    public async Task<ItemSummary> RestoreVersionAsync(VaultSession session, Guid id, int version, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        var row = repository.GetRow(id) ?? throw new KeyNotFoundException("Item not found.");
        if (row.TrashedAt is not null)
        {
            throw new ValidationException("ItemTrashed");
        }

        var oldEnvelope = repository.GetVersionPayload(id, version)
            ?? throw new KeyNotFoundException("Version not found.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Restoring appends the old state as a NEW version — the current version is retained.
        var payloadPlaintext = _encryption.DecryptToBytes(sessionLocal.Keyring, sessionLocal.Header.VaultId, id, oldEnvelope);
        byte[] resealed;
        try
        {
            resealed = _encryption.EncryptBytes(sessionLocal.Keyring, sessionLocal.Header.VaultId, id, payloadPlaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadPlaintext);
        }

        repository.Update(id, resealed, now);
        await PruneHistoryAsync(repository, id);
        return await GetSummaryAsync(sessionLocal, repository, id, cancellationToken);
    }

    public async Task<IReadOnlyList<CredentialSearchEntry>> BuildSearchIndexAsync(VaultSession session, ItemFilter? filter = null, CancellationToken cancellationToken = default)
    {
        var (sessionLocal, repository) = AssertActive(session);
        await Task.Yield();
        var organization = new OrganizationRepository(sessionLocal.Database);
        var folderNames = organization.ListFolders().ToDictionary(f => f.Id, f => DecryptMeta(sessionLocal, f.NameCipher, "folder:" + f.Id));
        var tagNames = organization.ListTags().ToDictionary(t => t.Id, t => DecryptMeta(sessionLocal, t.NameCipher, "tag:" + t.Id));

        var entries = new List<CredentialSearchEntry>();
        foreach (var row in SelectRows(repository, filter))
        {
            var envelope = repository.GetCurrentPayload(row.Id)
                ?? throw new DatabaseCorruptionException("Item payload missing.");
            var payload = ParsePayload(envelope, sessionLocal, row.Id);

            var kind = (ItemType)row.Type;
            var parts = new List<string> { payload.Title, payload.Description ?? string.Empty };
            if (ItemKindInfo.SearchableDataKeys.TryGetValue(kind, out var keys) && payload.Data is not null)
            {
                foreach (var key in keys)
                {
                    if (payload.Data.TryGetValue(key, out var value) && value is not null)
                    {
                        parts.Add(value.ToString() ?? string.Empty);
                    }
                }
            }

            foreach (var field in payload.Fields.Where(f => !f.Concealed))
            {
                parts.Add(field.Label);
                parts.Add(field.Value ?? string.Empty);
            }

            if (row.FolderId is { } folderId && folderNames.TryGetValue(folderId, out var folderName))
            {
                parts.Add(folderName);
            }

            foreach (var tagId in repository.GetTagIds(row.Id))
            {
                if (tagNames.TryGetValue(tagId, out var tagName))
                {
                    parts.Add(tagName);
                }
            }

            entries.Add(new CredentialSearchEntry(row.Id, string.Join('\n', parts)));
        }

        return entries;
    }

    // ── helpers ──

    private static (VaultSession Session, ItemRepository Repository) AssertActive(VaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Item operations require an unlocked vault session.");
        }

        return (session, new ItemRepository(session.Database));
    }

    private static IReadOnlyList<ItemRow> SelectRows(ItemRepository repository, ItemFilter? filter)
    {
        IEnumerable<ItemRow> rows = filter is { TrashedOnly: true } ? repository.ListTrashed() : repository.ListAllLive();
        if (filter is null)
        {
            return rows.ToList();
        }

        if (filter.Kind is { } kind)
        {
            rows = rows.Where(r => r.Type == (int)kind);
        }

        if (filter.FolderId is { } folderId)
        {
            rows = rows.Where(r => r.FolderId == folderId);
        }

        if (filter.FavoritesOnly)
        {
            rows = rows.Where(r => r.Favorite);
        }

        var tagSet = filter.TagIds is { Count: > 0 } ? filter.TagIds.ToHashSet() : null;
        if (tagSet is not null)
        {
            rows = rows.Where(r => repository.GetTagIds(r.Id).Any(tagSet.Contains));
        }

        if (filter.UpdatedAfter is { } after)
        {
            rows = rows.Where(r => r.UpdatedAt >= after.ToUnixTimeSeconds());
        }

        if (filter.UpdatedBefore is { } before)
        {
            rows = rows.Where(r => r.UpdatedAt <= before.ToUnixTimeSeconds());
        }

        return rows.ToList();
    }

    private static IReadOnlyList<ItemSummary> ApplySort(IReadOnlyList<ItemSummary> items, string? sortBy) =>
        sortBy switch
        {
            "title" => items.OrderBy(i => i.Title, StringComparer.CurrentCulture).ToList(),
            "created" => items.OrderByDescending(i => i.CreatedAt).ToList(),
            _ => items.OrderByDescending(i => i.UpdatedAt).ToList(),
        };

    private async Task<ItemSummary> GetSummaryAsync(VaultSession session, ItemRepository repository, Guid id, CancellationToken cancellationToken)
    {
        var row = repository.GetRow(id) ?? throw new KeyNotFoundException("Item not found.");
        var envelope = repository.GetCurrentPayload(id)
            ?? throw new DatabaseCorruptionException("Item payload missing.");
        var payload = ParsePayload(envelope, session, id);
        var kind = (ItemType)row.Type;
        return new ItemSummary(
            id, kind, payload.Title, ItemKindInfo.SubtitleFor(kind, payload.Data),
            row.Favorite, row.FolderId, repository.GetTagIds(id).ToList(),
            DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt),
            DateTimeOffset.FromUnixTimeSeconds(row.UpdatedAt));
    }

    private static void ApplyOrganization(
        VaultSession session, ItemRepository repository, Guid id, ItemDraft draft, bool keepTagsWhenNull)
    {
        repository.SetFolder(id, draft.FolderId);
        ApplyTags(session, repository, id, draft.TagIds, keepTagsWhenNull);
    }

    private static void ApplyTags(
        VaultSession session, ItemRepository repository, Guid id, IReadOnlyList<Guid> tagIds, bool keepTagsWhenNull)
    {
        if (tagIds is null && keepTagsWhenNull)
        {
            return;
        }

        repository.SetTags(id, tagIds ?? []);
        _ = session;
    }

    private static ItemPayload ToPayload(ItemDraft draft)
    {
        ValidateDraft(draft);
        return new ItemPayload
        {
            Title = draft.Title.Trim(),
            Description = draft.Description,
            Fields = draft.CustomFields.OrderBy(f => f.Order).ToList(),
            Data = draft.Data,
        };
    }

    private static void ValidateDraft(ItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new ValidationException("TitleRequired");
        }

        if (draft.Title.Length > 200)
        {
            throw new ValidationException("TitleTooLong");
        }

        foreach (var field in draft.CustomFields)
        {
            FieldValidation.Validate(field);
        }
    }

    private (Dictionary<string, object?> Data, List<CustomField> Fields) SplitPayload(ItemPayload payload)
    {
        var data = payload.Data is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(payload.Data);
        return (data, payload.Fields.OrderBy(f => f.Order).ToList());
    }

    /// <summary>Converts JsonElement values back into plain strings / string lists so consumers
    /// never see serializer artifacts.</summary>
    private static void NormalizeData(Dictionary<string, object?> data)
    {
        foreach (var key in data.Keys.ToList())
        {
            if (data[key] is System.Text.Json.JsonElement element)
            {
                data[key] = element.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Array => element.EnumerateArray()
                        .Select(e => e.ToString())
                        .ToList(),
                    System.Text.Json.JsonValueKind.True => "true",
                    System.Text.Json.JsonValueKind.False => "false",
                    _ => element.ToString(),
                };
            }
        }
    }

    private static ItemPayload ParsePayload(byte[] envelope, VaultSession session, Guid id)
    {
        var json = ItemEncryption.DecryptJson(session.Keyring, session.Header.VaultId, id, envelope);
        try
        {
            return ItemPayload.Parse(json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    private async Task PruneHistoryAsync(ItemRepository repository, Guid id)
    {
        await Task.Yield();
        var versions = repository.ListVersions(id).OrderByDescending(v => v.Version).ToList();
        foreach (var stale in versions.Skip(_historyRetentionPerItem))
        {
            repository.DeleteVersion(id, stale.Version);
        }
    }

    private static void DeleteAttachmentFile(VaultSession session, Guid attachmentId) =>
        File.Delete(Path.Combine(session.VaultDirectory, "attachments", attachmentId + ".pmblob"));

    private string DecryptMeta(VaultSession session, byte[] envelope, string recordId)
    {
        var plaintext = _encryption.DecryptMeta(session.Keyring, session.Header.VaultId, recordId, envelope);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
