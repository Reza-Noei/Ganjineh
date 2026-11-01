using PasswordManager.Cryptography.Aead;
using PasswordManager.Cryptography.Errors;
using Xunit;

namespace PasswordManager.Cryptography.Tests;

/// <summary>Raw chunked-AEAD protocol tests (attachment-encryption.md): ordering, truncation,
/// tamper, terminator — independent of file orchestration.</summary>
public class ChunkedAeadTests
{
    private static readonly byte[] Key = new byte[32];
    private static readonly byte[] VaultId = new byte[16];
    private static readonly byte[] AttachmentId = new byte[16];
    private static readonly byte[] Iv = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    [Fact]
    public void RoundTrip_MultiChunk_Preserves_Content_And_Order()
    {
        var chunks = new List<byte[]> { new byte[10], new byte[0], new byte[70000], new byte[3] };
        var total = chunks.Sum(c => c.Length);
        var encrypted = new List<(byte[] Ct, byte[] Tag)>();
        ulong index = 0;
        foreach (var chunk in chunks)
        {
            encrypted.Add(ChunkedAead.EncryptChunk(Key, Iv, index++, total, VaultId, AttachmentId, chunk));
        }

        // Terminator authenticates at index == chunks.Count.
        var (termCt, termTag) = ChunkedAead.EncryptChunk(Key, Iv, index, total, VaultId, AttachmentId, []);
        _ = (termCt, termTag);

        index = 0;
        foreach (var (ct, tag) in encrypted)
        {
            var plain = ChunkedAead.DecryptChunk(Key, Iv, index, total, VaultId, AttachmentId, ct, tag);
            Assert.Equal(chunks[(int)index], plain);
            index++;
        }
    }

    [Fact]
    public void Nonces_Differ_Per_Chunk_Index()
    {
        Assert.NotEqual(ChunkedAead.BuildChunkNonce(Iv, 0), ChunkedAead.BuildChunkNonce(Iv, 1));
        Assert.Equal(ChunkedAead.BuildChunkNonce(Iv, 0), ChunkedAead.BuildChunkNonce(Iv, 0));
        // u64 wrap affects the LOW bytes only.
        Assert.Equal(Iv[..4], ChunkedAead.BuildChunkNonce(Iv, 0x0102030405060708)[..4]);
    }

    [Fact]
    public void Reordered_Chunks_Fail_Authentication()
    {
        var (ct0, tag0) = ChunkedAead.EncryptChunk(Key, Iv, 0, 10, VaultId, AttachmentId, new byte[10]);
        var (ct1, tag1) = ChunkedAead.EncryptChunk(Key, Iv, 1, 10, VaultId, AttachmentId, new byte[10]);

        // Decrypt chunk 0's ciphertext with chunk 1's index → nonce/AAD mismatch → auth failure.
        Assert.Throws<CryptoAuthenticationException>(
            () => ChunkedAead.DecryptChunk(Key, Iv, 1, 10, VaultId, AttachmentId, ct0, tag0));
        Assert.Throws<CryptoAuthenticationException>(
            () => ChunkedAead.DecryptChunk(Key, Iv, 0, 10, VaultId, AttachmentId, ct1, tag1));
        _ = tag1;
    }

    [Fact]
    public void Different_Total_Size_Or_Attachment_Id_Fails()
    {
        var (ct, tag) = ChunkedAead.EncryptChunk(Key, Iv, 0, 10, VaultId, AttachmentId, new byte[10]);

        Assert.Throws<CryptoAuthenticationException>(
            () => ChunkedAead.DecryptChunk(Key, Iv, 0, 11, VaultId, AttachmentId, ct, tag));
        var otherAttachment = new byte[16];
        otherAttachment[0] = 9;
        Assert.Throws<CryptoAuthenticationException>(
            () => ChunkedAead.DecryptChunk(Key, Iv, 0, 10, VaultId, otherAttachment, ct, tag));
    }

    [Fact]
    public void Tampered_Ciphertext_Fails()
    {
        var (ct, tag) = ChunkedAead.EncryptChunk(Key, Iv, 0, 10, VaultId, AttachmentId, new byte[10]);
        ct[3] ^= 0x40;
        Assert.Throws<CryptoAuthenticationException>(
            () => ChunkedAead.DecryptChunk(Key, Iv, 0, 10, VaultId, AttachmentId, ct, tag));
    }
}
