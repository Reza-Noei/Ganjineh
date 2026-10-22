using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManager.Cryptography.Aead;

/// <summary>
/// Chunked authenticated-encryption protocol for large files (cryptographic-specification.md §8).
/// Layout of a .pmblob: magic(8) "GPBLB1\0\0" ‖ chunk_iv(12) ‖ total_size(i64 LE), then per chunk
/// [len(u32 LE)][ciphertext][tag(16)]; a final zero-length chunk authenticates end-of-file.
/// Nonce for chunk i = chunk_iv XOR u64(i) (TLS 1.3-style — structurally collision-free).
/// AAD = "GANCHK1" ‖ vault_id ‖ attachment_id ‖ chunk_index ‖ total_size — binds chunk identity,
/// ordering and the declared total length; truncation and reordering fail authentication.
/// </summary>
public static class ChunkedAead
{
    public const int ChunkSize = 4 * 1024 * 1024;
    public const int IvSize = 12;
    public const int TagSize = 16;
    public const int HeaderSize = 8 + IvSize + 8;
    public const int MaxAttachmentSize = 100 * 1024 * 1024;

    public static readonly byte[] FileMagic = "GPBLB1\0\0"u8.ToArray();
    private const string ChunkMagic = "GANCHK1";

    public static byte[] BuildChunkNonce(ReadOnlySpan<byte> chunkIv, ulong index)
    {
        if (chunkIv.Length != IvSize)
        {
            throw new ArgumentException("Chunk IV must be 96 bits.", nameof(chunkIv));
        }

        var nonce = chunkIv.ToArray();
        var tail = index;
        for (var i = 0; i < 8; i++)
        {
            nonce[IvSize - 1 - i] ^= (byte)tail;
            tail >>= 8;
        }

        return nonce;
    }

    public static byte[] BuildChunkAad(ReadOnlySpan<byte> vaultId, ReadOnlySpan<byte> attachmentId, ulong chunkIndex, long totalSize)
    {
        if (vaultId.Length != CryptoFormat.VaultIdSize || attachmentId.Length != CryptoFormat.VaultIdSize)
        {
            throw new ArgumentException("Vault and attachment IDs must be 128 bits.");
        }

        var aad = new byte[Encoding.ASCII.GetByteCount(ChunkMagic) + CryptoFormat.VaultIdSize + CryptoFormat.VaultIdSize + 8 + 8];
        var offset = 0;
        offset += Encoding.ASCII.GetBytes(ChunkMagic, aad);
        vaultId.CopyTo(aad.AsSpan(offset));
        offset += CryptoFormat.VaultIdSize;
        attachmentId.CopyTo(aad.AsSpan(offset));
        offset += CryptoFormat.VaultIdSize;
        BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(offset), chunkIndex);
        offset += 8;
        BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(offset), totalSize);
        return aad;
    }

    public static void WriteHeader(Stream output, ReadOnlySpan<byte> chunkIv, long totalSize)
    {
        output.Write(FileMagic);
        output.Write(chunkIv);
        var sizeBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, totalSize);
        output.Write(sizeBytes);
    }

    /// <summary>Encrypts one chunk (plaintext may be empty for the terminator).</summary>
    public static (byte[] Ciphertext, byte[] Tag) EncryptChunk(
        ReadOnlySpan<byte> attachmentKey, ReadOnlySpan<byte> chunkIv, ulong chunkIndex, long totalSize,
        ReadOnlySpan<byte> vaultId, ReadOnlySpan<byte> attachmentId, ReadOnlySpan<byte> plaintext)
    {
        var nonce = BuildChunkNonce(chunkIv, index: chunkIndex);
        var aad = BuildChunkAad(vaultId, attachmentId, chunkIndex, totalSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(attachmentKey, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        return (ciphertext, tag);
    }

    /// <summary>Decrypts one chunk; authentication failure raises the generic crypto error.</summary>
    public static byte[] DecryptChunk(
        ReadOnlySpan<byte> attachmentKey, ReadOnlySpan<byte> chunkIv, ulong chunkIndex, long totalSize,
        ReadOnlySpan<byte> vaultId, ReadOnlySpan<byte> attachmentId, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag)
    {
        var nonce = BuildChunkNonce(chunkIv, index: chunkIndex);
        var aad = BuildChunkAad(vaultId, attachmentId, chunkIndex, totalSize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(attachmentKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new Errors.CryptoAuthenticationException();
        }
    }
}
