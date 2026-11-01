using System.Text;
using Ganjineh.Cryptography;
using Ganjineh.Cryptography.Aead;
using Ganjineh.Cryptography.Keys;

namespace Ganjineh.Application.Credentials;

/// <summary>Login credential payload. Password handling follows the approved secure-memory
/// design: the payload travels as one authenticated envelope; decrypted JSON lives only in
/// session-scoped buffers (threat-model R-1 accepts in-memory exposure while unlocked).</summary>
public sealed record CredentialDraft
{
    public required string Title { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? Notes { get; init; }
}

/// <summary>List row — deliberately carries NO password material.</summary>
public sealed record CredentialSummary(
    Guid Id,
    string Title,
    string? Username,
    string? WebsiteUrl,
    bool Favorite,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Full record; the password is materialized only for reveal/copy/edit flows.</summary>
public sealed record CredentialDetail(
    Guid Id,
    string Title,
    string? Username,
    string? Password,
    string? WebsiteUrl,
    string? Notes,
    bool Favorite,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class CredentialValidation
{
    public const int MaxTitle = 200;
    public const int MaxUsername = 320;
    public const int MaxPassword = 1000;
    public const int MaxWebsiteUrl = 2048;
    public const int MaxNotes = 10_000;

    public static void Validate(CredentialDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new ValidationException("TitleRequired");
        }

        CheckLength(draft.Title, MaxTitle, "TitleTooLong");
        CheckLength(draft.Username, MaxUsername, "UsernameTooLong");
        CheckLength(draft.Password ?? string.Empty, MaxPassword, "PasswordTooLong");
        CheckLength(draft.WebsiteUrl, MaxWebsiteUrl, "UrlTooLong");
        CheckLength(draft.Notes, MaxNotes, "NotesTooLong");
    }

    /// <summary>Only http(s) URLs may be opened in a browser.</summary>
    public static bool IsSafeWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private static void CheckLength(string? value, int max, string code)
    {
        if ((value?.Length ?? 0) > max)
        {
            throw new ValidationException(code);
        }
    }
}

/// <summary>Input violates credential invariants; MessageKey localizes the reason.</summary>
public sealed class ValidationException(string messageKey) : Exception(messageKey);

/// <summary>
/// Encrypts/decrypts login payloads with the session's item key (crypto-specification.md §7):
/// envelope key_domain=Item, record_id = item UUID, purpose "payload.v1".
/// </summary>
public sealed class ItemEncryption
{
    public const string PayloadPurpose = "payload.v1";

    private readonly IAuthenticatedEncryptionService _aead;

    public ItemEncryption(IAuthenticatedEncryptionService? aead = null) =>
        _aead = aead ?? new AesGcmAuthenticatedEncryption();

    public byte[] EncryptPayload(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, CredentialDraft draft)
    {
        CredentialValidation.Validate(draft);
        var json = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(
            new PayloadDto(draft.Title, draft.Username, draft.Password, draft.WebsiteUrl, draft.Notes)));
        try
        {
            return _aead.Encrypt(json, keyring.ItemKey, KeyDomain.Item, Aad(vaultId, itemId)).Serialize();
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(json);
        }
    }

    public CredentialDetail DecryptPayload(
        VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, byte[] envelope,
        bool favorite, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        var payload = EncryptedPayload.Parse(envelope);
        var json = _aead.Decrypt(payload, keyring.ItemKey, Aad(vaultId, itemId));
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<PayloadDto>(json)
                ?? throw new Ganjineh.Persistence.DatabaseCorruptionException("Credential payload is empty.");
            return new CredentialDetail(
                itemId, dto.T ?? string.Empty, dto.U, dto.P ?? string.Empty, dto.W, dto.N,
                favorite, createdAt, updatedAt);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>Decrypts only the listing fields; the password never leaves the transient buffer.</summary>
    public (string Title, string? Username, string? WebsiteUrl) DecryptListingFields(
        VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, byte[] envelope)
    {
        var payload = EncryptedPayload.Parse(envelope);
        var json = _aead.Decrypt(payload, keyring.ItemKey, Aad(vaultId, itemId));
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<PayloadDto>(json)
                ?? throw new Ganjineh.Persistence.DatabaseCorruptionException("Credential payload is empty.");
            return (dto.T ?? string.Empty, dto.U, dto.W);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>
    /// Builds the in-memory search text for one item: title + username + URL + notes, WITHOUT the
    /// password (contract §7 — password content is never searchable). Never persisted.
    /// </summary>
    public string DecryptSearchText(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, byte[] envelope)
    {
        var payload = EncryptedPayload.Parse(envelope);
        var json = _aead.Decrypt(payload, keyring.ItemKey, Aad(vaultId, itemId));
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<PayloadDto>(json)
                ?? throw new Ganjineh.Persistence.DatabaseCorruptionException("Credential payload is empty.");
            return string.Join('\n', dto.T, dto.U, dto.W, dto.N);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>Serializes and seals a unified v1 payload (Application/Items model).</summary>
    public byte[] EncryptPayload(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, Ganjineh.Application.Items.ItemPayload payload)
    {
        var json = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload));
        try
        {
            return EncryptBytes(keyring, vaultId, itemId, json);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>Seals arbitrary plaintext under the item key with the payload binding.</summary>
    public byte[] EncryptBytes(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, ReadOnlySpan<byte> plaintext) =>
        _aead.Encrypt(plaintext, keyring.ItemKey, KeyDomain.Item, Aad(vaultId, itemId)).Serialize();

    /// <summary>Opens a payload envelope to raw JSON bytes (zeroized by the caller).</summary>
    public byte[] DecryptToBytes(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, byte[] envelope)
    {
        var payload = EncryptedPayload.Parse(envelope);
        return _aead.Decrypt(payload, keyring.ItemKey, Aad(vaultId, itemId));
    }

    /// <summary>Static variant for Phase 3 compatibility paths.</summary>
    public static byte[] DecryptJson(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, Guid itemId, byte[] envelope)
    {
        var payload = EncryptedPayload.Parse(envelope);
        return DefaultAead.Decrypt(payload, keyring.ItemKey, Aad(vaultId, itemId));
    }

    /// <summary>Seals metadata (folder/tag/vault names) under the item key, AAD-bound to the vault and record.</summary>
    public byte[] EncryptMeta(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, string recordId, ReadOnlySpan<byte> plaintext) =>
        _aead.Encrypt(plaintext, keyring.ItemKey, KeyDomain.Meta, MetaAad(vaultId, recordId)).Serialize();

    /// <summary>Opens a metadata envelope; returns raw plaintext bytes (zeroize after use).</summary>
    public byte[] DecryptMeta(VaultKeyring keyring, ReadOnlySpan<byte> vaultId, string recordId, byte[] envelope)
    {
        var payload = EncryptedPayload.Parse(envelope);
        return _aead.Decrypt(payload, keyring.ItemKey, MetaAad(vaultId, recordId));
    }

    private static byte[] MetaAad(ReadOnlySpan<byte> vaultId, string recordId)
    {
        var recordBytes = System.Text.Encoding.UTF8.GetBytes(recordId);
        var aad = new byte[vaultId.Length + recordBytes.Length];
        vaultId.CopyTo(aad);
        recordBytes.CopyTo(aad, vaultId.Length);
        return aad;
    }

    private static readonly IAuthenticatedEncryptionService DefaultAead = new AesGcmAuthenticatedEncryption();

    private static byte[] Aad(ReadOnlySpan<byte> vaultId, Guid itemId)
    {
        if (vaultId.Length != CryptoFormat.VaultIdSize)
        {
            throw new ArgumentException("Vault ID must be 128 bits.", nameof(vaultId));
        }

        var recordId = Encoding.UTF8.GetBytes(itemId.ToString());
        var aad = new byte[CryptoFormat.VaultIdSize + recordId.Length + Encoding.UTF8.GetByteCount(PayloadPurpose)];
        var offset = 0;
        vaultId.CopyTo(aad.AsSpan(offset));
        offset += CryptoFormat.VaultIdSize;
        recordId.CopyTo(aad, offset);
        offset += recordId.Length;
        Encoding.UTF8.GetBytes(PayloadPurpose, aad.AsSpan(offset));
        return aad;
    }

    private sealed record PayloadDto(string? T, string? U, string? P, string? W, string? N);
}
