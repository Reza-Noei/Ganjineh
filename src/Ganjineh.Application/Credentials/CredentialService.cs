using System.Globalization;
using PasswordManager.Cryptography.Aead;
using PasswordManager.Cryptography.Keys;
using PasswordManager.Persistence.Items;
using PasswordManager.Application.Vaults;

namespace PasswordManager.Application.Credentials;

/// <summary>
/// Login-credential use cases bound to an active vault session. Keys never leave the Application
/// layer; the repository only ever sees sealed envelopes (contract §6/§10).
/// </summary>
public interface ICredentialService
{
    Task<CredentialSummary> CreateAsync(VaultSession session, CredentialDraft draft, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CredentialSummary>> ListAsync(VaultSession session, CancellationToken cancellationToken = default);
    Task<CredentialDetail> GetAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);
    Task<CredentialSummary> UpdateAsync(VaultSession session, Guid id, CredentialDraft draft, CancellationToken cancellationToken = default);
    Task DeleteAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default);
    void SetFavorite(VaultSession session, Guid id, bool favorite);

    /// <summary>
    /// Builds the per-session in-memory search index: one entry per live item whose SearchText
    /// covers title/username/URL/notes and NEVER the password (contract §7).
    /// </summary>
    Task<IReadOnlyList<CredentialSearchEntry>> BuildSearchIndexAsync(VaultSession session, CancellationToken cancellationToken = default);
}

public sealed record CredentialSearchEntry(Guid Id, string SearchText);

public sealed class CredentialService : ICredentialService
{
    private readonly ItemEncryption _encryption;

    public CredentialService(ItemEncryption? encryption = null) =>
        _encryption = encryption ?? new ItemEncryption();

    public Task<CredentialSummary> CreateAsync(VaultSession session, CredentialDraft draft, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var repository = new ItemRepository(session.Database);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = Guid.NewGuid();
        var envelope = _encryption.EncryptPayload(session.Keyring, session.Header.VaultId, id, draft);
        repository.Insert(id, (int)ItemType.Login, envelope, now);
        return Task.FromResult(new CredentialSummary(
            id, draft.Title, draft.Username, draft.WebsiteUrl, Favorite: false,
            CreatedAt: DateTimeOffset.FromUnixTimeSeconds(now), UpdatedAt: DateTimeOffset.FromUnixTimeSeconds(now)));
    }

    public Task<IReadOnlyList<CredentialSummary>> ListAsync(VaultSession session, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var repository = new ItemRepository(session.Database);
        var summaries = new List<CredentialSummary>();
        foreach (var row in repository.ListRows((int)ItemType.Login))
        {
            var envelope = repository.GetCurrentPayload(row.Id)
                ?? throw new PasswordManager.Persistence.DatabaseCorruptionException("Credential payload missing.");
            var (title, username, website) = _encryption.DecryptListingFields(
                session.Keyring, session.Header.VaultId, row.Id, envelope);
            summaries.Add(new CredentialSummary(
                row.Id, title, username, website, row.Favorite,
                DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt),
                DateTimeOffset.FromUnixTimeSeconds(row.UpdatedAt)));
        }

        return Task.FromResult<IReadOnlyList<CredentialSummary>>(summaries);
    }

    public Task<CredentialDetail> GetAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var repository = new ItemRepository(session.Database);
        var row = repository.GetRow(id) ?? throw new KeyNotFoundException("Credential not found.");
        var envelope = repository.GetCurrentPayload(id)
            ?? throw new PasswordManager.Persistence.DatabaseCorruptionException("Credential payload missing.");
        var detail = _encryption.DecryptPayload(
            session.Keyring, session.Header.VaultId, id, envelope, row.Favorite,
            DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt),
            DateTimeOffset.FromUnixTimeSeconds(row.UpdatedAt));
        return Task.FromResult(detail);
    }

    public Task<CredentialSummary> UpdateAsync(VaultSession session, Guid id, CredentialDraft draft, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var repository = new ItemRepository(session.Database);
        var row = repository.GetRow(id) ?? throw new KeyNotFoundException("Credential not found.");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var envelope = _encryption.EncryptPayload(session.Keyring, session.Header.VaultId, id, draft);
        repository.Update(id, envelope, now);
        return Task.FromResult(new CredentialSummary(
            id, draft.Title, draft.Username, draft.WebsiteUrl, row.Favorite,
            DateTimeOffset.FromUnixTimeSeconds(row.CreatedAt), DateTimeOffset.FromUnixTimeSeconds(now)));
    }

    public Task DeleteAsync(VaultSession session, Guid id, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        new ItemRepository(session.Database).Delete(id);
        return Task.CompletedTask;
    }

    public void SetFavorite(VaultSession session, Guid id, bool favorite)
    {
        AssertActive(session);
        new ItemRepository(session.Database).SetFavorite(id, favorite, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public Task<IReadOnlyList<CredentialSearchEntry>> BuildSearchIndexAsync(VaultSession session, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var repository = new ItemRepository(session.Database);
        var entries = new List<CredentialSearchEntry>();
        foreach (var row in repository.ListRows((int)ItemType.Login))
        {
            var envelope = repository.GetCurrentPayload(row.Id)
                ?? throw new PasswordManager.Persistence.DatabaseCorruptionException("Credential payload missing.");
            entries.Add(new CredentialSearchEntry(
                row.Id,
                _encryption.DecryptSearchText(session.Keyring, session.Header.VaultId, row.Id, envelope)));
        }

        return Task.FromResult<IReadOnlyList<CredentialSearchEntry>>(entries);
    }

    private static void AssertActive(VaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Credential operations require an unlocked vault session.");
        }
    }
}
