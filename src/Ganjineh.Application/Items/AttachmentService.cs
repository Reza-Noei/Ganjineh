using System.Security.Cryptography;
using Ganjineh.Persistence;
using Ganjineh.Application.Credentials;
using Ganjineh.Application.Vaults;
using Ganjineh.Persistence.Items;
using Ganjineh.Cryptography;
using Ganjineh.Cryptography.Aead;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Cryptography.Keys;
using Ganjineh.Persistence.Organization;

namespace Ganjineh.Application.Items;

/// <summary>Attachment metadata for listings (filename decrypted, content never here).</summary>
public sealed record AttachmentInfo(Guid Id, Guid ItemId, string Filename, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>
/// Encrypted local attachments (contract §10): 4 MiB chunked AEAD per cryptographic-specification.md
/// §8, streamed to/from `<vault>/attachments/&lt;id&gt;.pmblob`. Never a plaintext temp file; the
/// metadata row is inserted only after the blob is durably complete (failure-safe ordering).
/// </summary>
public interface IAttachmentService
{
    Task<AttachmentInfo> AddAsync(VaultSession session, Guid itemId, string sourceFilePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttachmentInfo>> ListAsync(VaultSession session, Guid itemId, CancellationToken cancellationToken = default);
    Task ExportAsync(VaultSession session, Guid attachmentId, string destinationFilePath, CancellationToken cancellationToken = default);
    Task DeleteAsync(VaultSession session, Guid attachmentId, CancellationToken cancellationToken = default);
    /// <summary>Removes orphaned .part files and .pmblob blobs without a metadata row (called on vault open).</summary>
    int CleanupOrphans(VaultSession session);
}

public sealed class AttachmentService : IAttachmentService
{
    private readonly ItemEncryption _encryption;

    public AttachmentService(ItemEncryption? encryption = null) => _encryption = encryption ?? new ItemEncryption();

    private static string BlobPath(VaultSession session, Guid id) =>
        Path.Combine(session.VaultDirectory, "attachments", id + ".pmblob");

    private static OrganizationRepository Repo(VaultSession session) => new(session.Database);

    public async Task<AttachmentInfo> AddAsync(VaultSession session, Guid itemId, string sourceFilePath, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var repository = new ItemRepository(session.Database);
        var row = repository.GetRow(itemId) ?? throw new KeyNotFoundException("Item not found.");
        if (row.TrashedAt is not null)
        {
            throw new ValidationException("ItemTrashed");
        }

        var fileInfo = new FileInfo(sourceFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Attachment source not found.", sourceFilePath);
        }

        if (fileInfo.Length > ChunkedAead.MaxAttachmentSize)
        {
            throw new ValidationException("AttachmentTooLarge");
        }

        var id = Guid.NewGuid();
        var directory = Path.Combine(session.VaultDirectory, "attachments");
        Directory.CreateDirectory(directory);
        var finalPath = BlobPath(session, id);
        var partPath = finalPath + ".part";

        var fileKey = RandomNumberGenerator.GetBytes(CryptoFormat.KeySize);
        var chunkIv = RandomNumberGenerator.GetBytes(ChunkedAead.IvSize);
        var totalSize = fileInfo.Length;

        try
        {
            await using (var source = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                ChunkedAead.ChunkSize, useAsync: true))
            await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                ChunkedAead.ChunkSize, useAsync: true))
            {
                ChunkedAead.WriteHeader(target, chunkIv, totalSize);
                var buffer = new byte[ChunkedAead.ChunkSize];
                ulong index = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    var (ciphertext, tag) = ChunkedAead.EncryptChunk(
                        session.Keyring.AttachmentKey, chunkIv, index, totalSize,
                        session.Header.VaultId, id.ToByteArray(), buffer.AsSpan(0, read));
                    await WriteChunkAsync(target, ciphertext, tag);
                    index++;
                }

                // Terminator: zero-length chunk authenticates the end of the stream.
                var (emptyCt, emptyTag) = ChunkedAead.EncryptChunk(
                    session.Keyring.AttachmentKey, chunkIv, index, totalSize,
                    session.Header.VaultId, id.ToByteArray(), ReadOnlySpan<byte>.Empty);
                await WriteChunkAsync(target, emptyCt, emptyTag);
                await target.FlushAsync(cancellationToken);
                target.Flush(flushToDisk: true);
            }

            // Blob complete and durable → move into place, then persist metadata.
            File.Move(partPath, finalPath);
            var filenameEnvelope = _encryption.EncryptMeta(
                session.Keyring, session.Header.VaultId, "att-name:" + id.ToString(),
                System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(sourceFilePath)));
            var fileKeyEnvelope = SealFileKey(session, id, fileKey);

            var info = new AttachmentInfo(
                id, itemId, Path.GetFileName(sourceFilePath), totalSize, DateTimeOffset.UtcNow);
            var metaRow = new AttachmentRow(
                id, itemId, filenameEnvelope, totalSize, fileKeyEnvelope, info.CreatedAt.ToUnixTimeSeconds());
            Repo(session).AddAttachment(metaRow);
            return info;
        }
        catch
        {
            try { File.Delete(partPath); } catch (IOException) { }
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
            CryptographicOperations.ZeroMemory(chunkIv);
        }
    }

    public async Task<IReadOnlyList<AttachmentInfo>> ListAsync(VaultSession session, Guid itemId, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        return Repo(session).ListAttachments(itemId)
            .Select(a => new AttachmentInfo(
                a.Id, a.ItemId, OpenName(session, a.Id, a.FilenameCipher),
                a.SizeBytes, DateTimeOffset.FromUnixTimeSeconds(a.CreatedAt)))
            .ToList();
    }

    public async Task ExportAsync(VaultSession session, Guid attachmentId, string destinationFilePath, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var meta = Repo(session).GetAttachment(attachmentId)
            ?? throw new KeyNotFoundException("Attachment not found.");

        var fileKey = OpenFileKey(session, attachmentId, meta.FileKeyCipher);
        try
        {
            await using var source = new FileStream(BlobPath(session, attachmentId), FileMode.Open,
                FileAccess.Read, FileShare.Read, ChunkedAead.ChunkSize, useAsync: true);
            var header = new byte[ChunkedAead.HeaderSize];
            await source.ReadExactlyAsync(header.AsMemory(0, header.Length), cancellationToken);
            var chunkIv = header.AsSpan(8, ChunkedAead.IvSize).ToArray();
            var declaredTotal = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8 + ChunkedAead.IvSize));

            await using var target = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write,
                FileShare.None, ChunkedAead.ChunkSize, useAsync: true);
            var lengthPrefix = new byte[4];
            ulong index = 0;
            long written = 0;
            while (true)
            {
                await source.ReadExactlyAsync(lengthPrefix.AsMemory(), cancellationToken);
                var cipherLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
                var cipherText = new byte[cipherLength];
                if (cipherLength > 0)
                {
                    await source.ReadExactlyAsync(cipherText.AsMemory(), cancellationToken);
                }

                var tag = new byte[ChunkedAead.TagSize];
                await source.ReadExactlyAsync(tag.AsMemory(), cancellationToken);

                if (cipherLength == 0)
                {
                    // Terminator authenticates completeness; total size must match the header.
                    ChunkedAead.DecryptChunk(session.Keyring.AttachmentKey, chunkIv, index, declaredTotal,
                        session.Header.VaultId, attachmentId.ToByteArray(), ReadOnlySpan<byte>.Empty, tag);
                    break;
                }

                var plaintext = ChunkedAead.DecryptChunk(session.Keyring.AttachmentKey, chunkIv, index, declaredTotal,
                    session.Header.VaultId, attachmentId.ToByteArray(), cipherText, tag);
                try
                {
                    await target.WriteAsync(plaintext.AsMemory(0, plaintext.Length), cancellationToken);
                    written += plaintext.Length;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }

                index++;
            }

            if (written != declaredTotal)
            {
                throw new DatabaseCorruptionException("Attachment length mismatch.");
            }

            await target.FlushAsync(cancellationToken);
        }
        catch
        {
            try { File.Delete(destinationFilePath); } catch (IOException) { }
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    public async Task DeleteAsync(VaultSession session, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        await Task.Yield();
        Repo(session).DeleteAttachment(attachmentId);
        try { File.Delete(BlobPath(session, attachmentId)); } catch (IOException) { }
    }

    public int CleanupOrphans(VaultSession session)
    {
        var directory = Path.Combine(session.VaultDirectory, "attachments");
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var known = Repo(session).ListAllAttachmentIds().Select(id => id + ".pmblob").ToHashSet();
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".part", StringComparison.Ordinal)
                || (name.EndsWith(".pmblob", StringComparison.Ordinal) && !known.Contains(name)))
            {
                try { File.Delete(file); removed++; } catch (IOException) { }
            }
        }

        return removed;
    }

    // ── helpers ──

    private static async Task WriteChunkAsync(Stream target, byte[] ciphertext, byte[] tag)
    {
        var prefix = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(prefix, ciphertext.Length);
        await target.WriteAsync(prefix.AsMemory());
        await target.WriteAsync(ciphertext.AsMemory());
        await target.WriteAsync(tag.AsMemory());
    }

    private byte[] SealFileKey(VaultSession session, Guid attachmentId, ReadOnlySpan<byte> fileKey)
    {
        var aad = BuildAttachmentAad(session, attachmentId);
        using var aes = new AesGcm(session.Keyring.AttachmentKey, ChunkedAead.TagSize);
        var nonce = RandomNumberGenerator.GetBytes(ChunkedAead.IvSize);
        var ciphertext = new byte[fileKey.Length];
        var tag = new byte[ChunkedAead.TagSize];
        aes.Encrypt(nonce, fileKey, ciphertext, tag, aad);
        var sealedKey = new byte[ciphertext.Length + ChunkedAead.TagSize];
        ciphertext.CopyTo(sealedKey, 0);
        tag.CopyTo(sealedKey, ciphertext.Length);
        var nonceCopy = nonce;
        var result = new byte[nonceCopy.Length + sealedKey.Length];
        nonceCopy.CopyTo(result, 0);
        sealedKey.CopyTo(result, nonceCopy.Length);
        return result;
    }

    private byte[] OpenFileKey(VaultSession session, Guid attachmentId, byte[] sealedKey)
    {
        var nonce = sealedKey.AsSpan(0, ChunkedAead.IvSize);
        var ciphertext = sealedKey.AsSpan(ChunkedAead.IvSize, sealedKey.Length - ChunkedAead.IvSize - ChunkedAead.TagSize);
        var tag = sealedKey.AsSpan(sealedKey.Length - ChunkedAead.TagSize);
        var aad = BuildAttachmentAad(session, attachmentId);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(session.Keyring.AttachmentKey, ChunkedAead.TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptoAuthenticationException();
        }
    }

    private static byte[] BuildAttachmentAad(VaultSession session, Guid attachmentId)
    {
        var record = System.Text.Encoding.UTF8.GetBytes("att-filekey:" + attachmentId.ToString());
        var aad = new byte[session.Header.VaultId.Length + record.Length];
        session.Header.VaultId.CopyTo(aad, 0);
        record.CopyTo(aad, session.Header.VaultId.Length);
        return aad;
    }

    private string OpenName(VaultSession session, Guid attachmentId, byte[] envelope) =>
        System.Text.Encoding.UTF8.GetString(_encryption.DecryptMeta(
            session.Keyring, session.Header.VaultId, "att-name:" + attachmentId.ToString(), envelope));

    private static void AssertActive(VaultSession session)
    {
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Attachment operations require an unlocked vault session.");
        }
    }
}
