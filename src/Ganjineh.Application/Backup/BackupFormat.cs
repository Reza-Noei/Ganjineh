using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ganjineh.Cryptography;
using Ganjineh.Cryptography.Aead;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Cryptography.Generation;

namespace Ganjineh.Application.Backup;

/// <summary>
/// `.gvbak` v1 wire format (database-design.md §7, extended for Phase 4 attachments):
///
///   [0x00] magic "GPBAK1\0"                       (8 B, plaintext-visible)
///   [0x08] format_version u16 = 1                 (plaintext-visible)
///   [0x0A] backup_id                              (16 B UUID, plaintext-visible)
///   [0x1A] vault_id                               (16 B UUID, plaintext-visible)
///   [0x2A] created_at_unix                        (i64 LE, plaintext-visible)
///   [0x32] header_len u32 = 512
///   [0x36] header.bin copy (512 B)                — the source vault's authenticated header
///                                                  (KDF params + password/recovery wraps;
///                                                  restores unlock with the SAME credentials)
///   meta_len u32 + meta_region:
///       meta_nonce(12) + AES-GCM(backup_wrap_key, meta_json, tag) with
///       AAD = ALL preceding bytes of the file (magic…header)  → header metadata authenticated
///   meta_json (decrypted): BackupManifest (counts, sha256s, attachment index)
///   package_len u64 + package:
///       GPBLB1 chunked envelope (ChunkedAead) under backup_wrap_key; AAD of every chunk binds
///       vault_id ‖ backup_id ‖ chunk index ‖ total package size. Package plaintext =
///       GPKG container: "GPKG1\0" + count + entries {name, bytes} (vault.db + .pmblob blobs).
///
/// Plaintext-visible before decryption: magic, version, ids, timestamp, the authenticated vault
/// header (KDF params + wrapped keys — no secrets). Everything else is authenticated+encrypted.
/// </summary>
public static class BackupFormat
{
    public const ushort FormatVersion = 1;
    public const int FixedHeaderSize = 0x36; // 54 bytes before the header copy

    public static readonly byte[] Magic = "GPBAK1\0"u8.ToArray();

    public static void WriteFixedHeader(
        Span<byte> destination, Guid backupId, byte[] vaultId, long createdAtUnix, int headerLength)
    {
        Magic.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], FormatVersion);
        backupId.ToByteArray().CopyTo(destination[10..]);
        vaultId.CopyTo(destination[26..]);
        BinaryPrimitives.WriteInt64LittleEndian(destination[42..], createdAtUnix);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[50..], (uint)headerLength);
    }

    public static (Guid BackupId, byte[] VaultId, long CreatedAtUnix, int HeaderLength, ushort Version)
        ParseFixedHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedHeaderSize || !bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new CryptoFormatException("Not a Ganjineh backup file.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
        if (version != FormatVersion)
        {
            throw new UnsupportedFormatException($"Backup format {version} is not supported.");
        }

        return (
            new Guid(bytes[10..26].ToArray()),
            bytes[26..42].ToArray(),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[42..]),
            checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[50..])),
            version);
    }

    /// <summary>Authenticated metadata (opened with backup_wrap_key; AAD covers the file prefix).</summary>
    public sealed record BackupManifest
    {
        public required Guid BackupId { get; init; }
        public required Guid VaultId { get; init; }
        public required long CreatedAtUnix { get; init; }
        public required int SchemaVersion { get; init; }
        public required int ItemCount { get; init; }
        public required int AttachmentCount { get; init; }
        public required string DbSha256 { get; init; }
        public required long PackageSize { get; init; }
        public required List<AttachmentIndexEntry> Attachments { get; init; }
    }

    public sealed record AttachmentIndexEntry(Guid AttachmentId, Guid ItemId, string Filename, long SizeBytes, string Sha256);

    public static byte[] SerializeManifest(BackupManifest manifest) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));

    public static BackupManifest DeserializeManifest(byte[] json) =>
        JsonSerializer.Deserialize<BackupManifest>(json)
        ?? throw new CryptoFormatException("Backup manifest is empty.");

    /// <summary>Inner package container: vault.db + encrypted attachment blobs (as-is, never re-encrypted).</summary>
    public sealed class PackageBuilder
    {
        private readonly List<(string Name, Func<Stream> Open, long Length)> _entries = [];

        public void Add(string name, byte[] bytes) =>
            Add(name, () => new MemoryStream(bytes, writable: false), bytes.Length);

        public void Add(string name, string filePath) =>
            Add(name, () => new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read), new FileInfo(filePath).Length);

        public void Add(string name, Func<Stream> open, long length) =>
            _entries.Add((name, open, length));

        /// <summary>Exact total byte length of the container (needed up-front for chunk AAD).</summary>
        public long ComputeSize()
        {
            long size = 6 + 4; // magic + count
            foreach (var (name, _, length) in _entries)
            {
                size += 4 + Encoding.UTF8.GetByteCount(name) + 8 + length;
            }

            return size;
        }

        public void WriteTo(Stream output)
        {
            output.Write("GPKG1\0"u8);
            var countBytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(countBytes, _entries.Count);
            output.Write(countBytes);

            foreach (var (name, open, length) in _entries)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                var lenBytes = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(lenBytes, nameBytes.Length);
                output.Write(lenBytes);
                output.Write(nameBytes);
                var sizeBytes = new byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, length);
                output.Write(sizeBytes);

                using var source = open();
                source.CopyTo(output);
            }
        }

        public IEnumerable<(string Name, long Length)> EntryHeaders() =>
            _entries.Select(e => (e.Name, e.Length));
    }

    /// <summary>Reads a GPKG container fully into named byte buffers (bounded by maxTotal).</summary>
    public static Dictionary<string, byte[]> ReadPackage(Stream input, long maxTotal)
    {
        using var bounded = new BoundedStream(input, maxTotal);
        var magic = new byte[6];
        bounded.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual("GPKG1\0"u8.ToArray()))
        {
            throw new CryptoFormatException("Backup package magic mismatch.");
        }

        var countBytes = new byte[4];
        bounded.ReadExactly(countBytes);
        var count = BinaryPrimitives.ReadInt32LittleEndian(countBytes);
        if (count is < 0 or > 10_000)
        {
            throw new CryptoFormatException("Backup package entry count out of range.");
        }

        var entries = new Dictionary<string, byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var nameLenBytes = new byte[4];
            bounded.ReadExactly(nameLenBytes);
            var nameLen = BinaryPrimitives.ReadInt32LittleEndian(nameLenBytes);
            if (nameLen is < 0 or > 4096)
            {
                throw new CryptoFormatException("Backup entry name length out of range.");
            }

            var nameBytes = new byte[nameLen];
            bounded.ReadExactly(nameBytes);
            var sizeBytes = new byte[8];
            bounded.ReadExactly(sizeBytes);
            var size = BinaryPrimitives.ReadInt64LittleEndian(sizeBytes);
            if (size < 0 || size > maxTotal)
            {
                throw new CryptoFormatException("Backup entry length out of range.");
            }

            var bytes = new byte[size];
            bounded.ReadExactly(bytes);
            entries[Encoding.UTF8.GetString(nameBytes)] = bytes;
        }

        return entries;
    }
}

/// <summary>Stream wrapper with a hard byte cap — bounds allocation from untrusted lengths.</summary>
public sealed class BoundedStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    public BoundedStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _remaining = maxBytes;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _remaining;
    public override long Position { get => 0; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var toRead = (int)Math.Min(count, _remaining);
        if (toRead == 0)
        {
            return 0;
        }

        var read = _inner.Read(buffer, offset, toRead);
        _remaining -= read;
        return read;
    }

    public void ReadExactly(byte[] buffer)
    {
        if (_remaining < buffer.Length)
        {
            throw new CryptoFormatException("Backup package truncated.");
        }

        var read = Read(buffer, 0, buffer.Length);
        if (read != buffer.Length)
        {
            throw new CryptoFormatException("Backup package truncated.");
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
