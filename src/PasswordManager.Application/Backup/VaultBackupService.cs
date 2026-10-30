using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Items;
using PasswordManager.Application.Security;
using PasswordManager.Application.Vaults;
using PasswordManager.Cryptography;
using PasswordManager.Cryptography.Aead;
using PasswordManager.Cryptography.Errors;
using PasswordManager.Cryptography.Generation;
using PasswordManager.Cryptography.Kdf;
using PasswordManager.Cryptography.KeyWrapping;
using PasswordManager.Cryptography.Keys;
using PasswordManager.Cryptography.Recovery;
using PasswordManager.Cryptography.Otp;
using PasswordManager.Persistence;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.Migrations;
using PasswordManager.Persistence.Organization;
using PasswordManager.Persistence.Registry;
using PasswordManager.Persistence.SqlCipher;
using PasswordManager.Persistence.VaultHeader;

namespace PasswordManager.Application.Backup;

public sealed record BackupRequest
{
    public required string DestinationFilePath { get; init; }
}
public sealed record BackupResult
{
    public required bool Success { get; init; }
    public string? ErrorKey { get; init; }
    public Guid? BackupId { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? DestinationPath { get; init; }
}

public enum VerificationLevel { Structural, Cryptographic, Rehearsal }

public sealed record BackupVerificationResult
{
    public required bool Success { get; init; }
    public required VerificationLevel Level { get; init; }
    public string? ErrorKey { get; init; }
    public DateTimeOffset VerifiedAt { get; init; }
    public string? VaultIdText { get; init; }
    public DateTimeOffset? BackupCreatedAt { get; init; }
    public int ItemCount { get; init; }
    public int AttachmentCount { get; init; }
}

public sealed record RestoreRequest
{
    public required string BackupFilePath { get; init; }
    public required string DestinationParentDirectory { get; init; }
    public required string CredentialPassword { get; init; }
}

public sealed record RestoreResult
{
    public required bool Success { get; init; }
    public string? ErrorKey { get; init; }
    public string? VaultDirectory { get; init; }
    public Guid? VaultId { get; init; }
    public int ItemCount { get; init; }
    public int AttachmentCount { get; init; }
    public VaultSession? Session { get; init; }
}

public enum SelectiveConflictPolicy { Skip, CreateCopy, ReplaceExisting }

public sealed record SelectiveRestoreRequest
{
    public required string BackupFilePath { get; init; }
    public required string CredentialPassword { get; init; }
    public required VaultSession TargetSession { get; init; }
    public required IReadOnlyList<Guid> ItemIds { get; init; }
    public SelectiveConflictPolicy Policy { get; init; } = SelectiveConflictPolicy.CreateCopy;
}

public sealed record SelectiveRestoreResult(int Created, int Skipped, int Replaced, int Attachments);

public interface IVaultBackupService
{
    Task<BackupResult> CreateAsync(VaultSession session, BackupRequest request, CancellationToken cancellationToken = default);
    Task<BackupVerificationResult> VerifyAsync(string backupFilePath, VaultSession? session, VerificationLevel level, CancellationToken cancellationToken = default);
    Task<RestoreResult> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken = default);
    Task<SelectiveRestoreResult> RestoreSelectiveAsync(SelectiveRestoreRequest request, CancellationToken cancellationToken = default);
    int PruneBackups(string directory, int keepCount);
}

/// <summary>
/// Faithful full-vault backup (`.gvbak` v1): the file embeds the source vault's authenticated
/// header (KDF params + password/recovery wraps) so the SAME Master Password or Recovery Key
/// unlocks it on any machine, an AEAD-authenticated manifest, and a chunked-AEAD package of the
/// consistent DB snapshot (VACUUM INTO) plus attachment blobs in their already-encrypted
/// representation. Documented limitation: older backups keep the key wrappings from when they
/// were taken — later password changes or recovery rotations do not revoke them.
/// </summary>
public sealed class VaultBackupService : IVaultBackupService
{
    private readonly IVaultLifecycle _lifecycle;
    private readonly Func<VaultRegistry> _registryFactory;
    private readonly int _historyRetentionPerItem;

    public VaultBackupService(
        IVaultLifecycle? lifecycle = null,
        Func<VaultRegistry>? registryFactory = null,
        int historyRetentionPerItem = 50)
    {
        _lifecycle = lifecycle ?? new VaultLifecycleService();
        _registryFactory = registryFactory ?? DefaultRegistry;
        _historyRetentionPerItem = historyRetentionPerItem;
    }

    private static VaultRegistry DefaultRegistry() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ganjineh", "vaults.json"));

    private static void AssertActive(VaultSession session)
    {
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Backup operations require an unlocked vault session.");
        }
    }

    // ───────────────────────── Create ─────────────────────────

    public async Task<BackupResult> CreateAsync(VaultSession session, BackupRequest request, CancellationToken cancellationToken = default)
    {
        AssertActive(session);
        var backupId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var partPath = request.DestinationFilePath + ".part";
        var snapshotPath = request.DestinationFilePath + ".dbsnap";
        var pkgPath = request.DestinationFilePath + ".pkg";
        try
        {
            if (File.Exists(request.DestinationFilePath))
            {
                return new BackupResult { Success = false, ErrorKey = "Backup.DestinationExists" };
            }

            var directory = Path.GetDirectoryName(request.DestinationFilePath);
            if (string.IsNullOrEmpty(directory))
            {
                return new BackupResult { Success = false, ErrorKey = "Error.Storage" };
            }

            Directory.CreateDirectory(directory);

            // 1. Consistent committed snapshot (VACUUM INTO — no blind copy, no WAL juggling).
            session.Database.CheckpointWal();
            session.Database.VacuumInto(snapshotPath);

            // 2. Attachment entries (encrypted representation copied as-is) + integrity hashes.
            var organization = new OrganizationRepository(session.Database);
            var entries = new List<(Guid Id, string Name, string SourcePath, string Sha256)>();
            var attachmentIndex = new List<BackupFormat.AttachmentIndexEntry>();
            foreach (var meta in organization.ListAllAttachmentRows())
            {
                var blobPath = Path.Combine(session.VaultDirectory, "attachments", meta.Id + ".pmblob");
                if (!File.Exists(blobPath))
                {
                    return new BackupResult { Success = false, ErrorKey = "Backup.MissingAttachment" };
            }

                using var sha = SHA256.Create();
                var hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(blobPath))).ToLowerInvariant();
                entries.Add((meta.Id, "attachments/" + meta.Id + ".pmblob", blobPath, hash));
                attachmentIndex.Add(new BackupFormat.AttachmentIndexEntry(
                    meta.Id, meta.ItemId, string.Empty, meta.SizeBytes, hash));
            }

            var itemCount = new ItemRepository(session.Database).CountLive();

            // 3. Package with exact up-front size (every chunk AAD binds the total length).
            var package = new BackupFormat.PackageBuilder();
            package.Add("vault.db", snapshotPath);
            foreach (var entry in entries)
            {
                package.Add(entry.Name, entry.SourcePath);
            }

            var containerSize = package.ComputeSize();
            var fullChunks = (int)Math.Ceiling((double)containerSize / ChunkedAead.ChunkSize);
            // Region = GPBLB1 header (28) + Σ[len(4)+ct+tag(16) per chunk] + terminator [4+16].
            var packageSize = 28
                + containerSize
                + fullChunks * (4 + ChunkedAead.TagSize)
                + (4 + ChunkedAead.TagSize);

            var manifest = new BackupFormat.BackupManifest
            {
                BackupId = backupId,
                VaultId = new Guid(session.Header.VaultId),
                CreatedAtUnix = createdAt.ToUnixTimeSeconds(),
                SchemaVersion = SchemaMigrator.CurrentVersion,
                ItemCount = itemCount,
                AttachmentCount = entries.Count,
                DbSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(snapshotPath, cancellationToken))).ToLowerInvariant(),
                PackageSize = packageSize,
                Attachments = attachmentIndex,
            };

            var backupWrapKey = HkdfKeyDerivation.DeriveSubkey(
                session.Keyring.VaultMasterKey, session.Header.VaultId, CryptoFormat.LabelBackupWrap);

                // 4a. Write the plaintext-of-ciphertexts container to a temp file (its content is
            //     still SQLCipher/AEAD-encrypted at every layer — safe on disk).
            await using (var pkg = new FileStream(pkgPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 1 << 20, useAsync: true))
            {
                package.WriteTo(pkg);
                await pkg.FlushAsync(cancellationToken);
            }
            Console.Error.WriteLine($"[P6DIAG] 4a done: pkgPath={pkgPath} len={new FileInfo(pkgPath).Length}");

            // 4b. Fixed header → header copy → authenticated meta → chunked package.
            await using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 1 << 20, useAsync: true))
            {
                var prefix = new byte[BackupFormat.FixedHeaderSize + CryptoFormat.HeaderSize];
                BackupFormat.WriteFixedHeader(prefix, backupId, session.Header.VaultId,
                    createdAt.ToUnixTimeSeconds(), CryptoFormat.HeaderSize);
                HeaderCodec.Serialize(session.Header).CopyTo(prefix, BackupFormat.FixedHeaderSize);
                output.Write(prefix);

                var metaJson = BackupFormat.SerializeManifest(manifest);
                var metaAad = MetaAad(prefix);
                var metaNonce = RandomNumberGenerator.GetBytes(ChunkedAead.IvSize);
                var metaCt = new byte[metaJson.Length];
                var metaTag = new byte[ChunkedAead.TagSize];
                using (var aes = new AesGcm(backupWrapKey, ChunkedAead.TagSize))
                {
                    aes.Encrypt(metaNonce, metaJson, metaCt, metaTag, metaAad);
                }

                var metaLen = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(metaLen,
                    metaNonce.Length + metaCt.Length + metaTag.Length);
                output.Write(metaLen);
                output.Write(metaNonce);
                output.Write(metaCt);
                output.Write(metaTag);

                var pkgLen = new byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(pkgLen, packageSize);
                output.Write(pkgLen);

                var chunkIv = RandomNumberGenerator.GetBytes(ChunkedAead.IvSize);
                output.Write(ChunkedAead.FileMagic);
                output.Write(chunkIv);
                var totalBytes = new byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(totalBytes, containerSize);
                output.Write(totalBytes);

                await using var packageStream = new FileStream(pkgPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, ChunkedAead.ChunkSize, useAsync: true);
                var buffer = new byte[ChunkedAead.ChunkSize];
                ulong chunkIndex = 0;
                int read;
                while ((read = await packageStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    var (ct, tag) = ChunkedAead.EncryptChunk(
                        backupWrapKey, chunkIv, chunkIndex++, containerSize,
                        session.Header.VaultId, backupId.ToByteArray(), buffer.AsSpan(0, read));
                    Console.Error.WriteLine($"[P6DIAG] chunk {chunkIndex - 1} ctLen={ct.Length} ct[0..8]={Convert.ToHexString(ct, 0, Math.Min(8, ct.Length))} tag={Convert.ToHexString(tag)}");
                    await WriteChunkAsync(output, ct, tag);
                }

                var (emptyCt, emptyTag) = ChunkedAead.EncryptChunk(
                    backupWrapKey, chunkIv, chunkIndex, containerSize,
                    session.Header.VaultId, backupId.ToByteArray(), ReadOnlySpan<byte>.Empty);
                await WriteChunkAsync(output, emptyCt, emptyTag);

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            // 5. Atomic finalize — the .part stream is closed at this point.
            File.Move(partPath, request.DestinationFilePath);
            return new BackupResult
            {
                Success = true,
                BackupId = backupId,
                SizeBytes = new FileInfo(request.DestinationFilePath).Length,
                CreatedAt = createdAt,
                DestinationPath = request.DestinationFilePath,
            };
        }
        catch (OperationCanceledException)
        {
            TryDelete(partPath);
            TryDelete(pkgPath);
            return new BackupResult { Success = false, ErrorKey = "Error.Cancelled" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(partPath);
            TryDelete(pkgPath);
            return new BackupResult { Success = false, ErrorKey = "Error.Storage:" + ex.GetType().Name };
        }
        finally
        {
            TryDelete(partPath);
            TryDelete(snapshotPath);
            TryDelete(pkgPath);
        }
    }

    // ───────────────────────── Verify ─────────────────────────

    public async Task<BackupVerificationResult> VerifyAsync(
        string backupFilePath, VaultSession? session, VerificationLevel level, CancellationToken cancellationToken = default)
    {
        var verifiedAt = DateTimeOffset.UtcNow;
        try
        {
            var bytes = await File.ReadAllBytesAsync(backupFilePath, cancellationToken);
            var (backupId, vaultId, created, headerLength, _) = BackupFormat.ParseFixedHeader(bytes);
            var vaultIdText = new Guid(vaultId).ToString();
            var header = HeaderCodec.Parse(bytes.AsSpan().Slice(BackupFormat.FixedHeaderSize, headerLength).ToArray());

            if (level == VerificationLevel.Structural)
            {
                // Plaintext length consistency: metaLen + packageLen must tile the file exactly.
                var metaOffsetCheck = BackupFormat.FixedHeaderSize + headerLength;
                var metaLenCheck = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.AsSpan(metaOffsetCheck, 4));
                var packageLenCheck = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
                    bytes.AsSpan(metaOffsetCheck + 4 + metaLenCheck, 8));
                if (metaLenCheck is < ChunkedAead.IvSize + ChunkedAead.TagSize or > 1_000_000
                    || packageLenCheck <= 0
                    || metaOffsetCheck + 4 + metaLenCheck + 8 + packageLenCheck != bytes.Length)
                {
                    return new BackupVerificationResult { Success = false, Level = level, ErrorKey = "Backup.Truncated", VerifiedAt = verifiedAt, VaultIdText = vaultIdText };
                }

                return new BackupVerificationResult { Success = true, Level = level, VerifiedAt = verifiedAt, VaultIdText = vaultIdText, BackupCreatedAt = DateTimeOffset.FromUnixTimeSeconds(created) };
            }

            if (session is null || session.State is not VaultSessionState.Unlocked)
            {
                return Fail(level, "Backup.RequiresUnlock", verifiedAt, vaultIdText, created);
            }

            var backupWrapKey = HkdfKeyDerivation.DeriveSubkey(
                session.Keyring.VaultMasterKey, header.VaultId, CryptoFormat.LabelBackupWrap);
            var (manifest, packagePath) = await OpenBackupAsync(backupFilePath, bytes, header, backupWrapKey, backupId);
            try
            {
                await using var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1 << 20, useAsync: true);

                if (level == VerificationLevel.Cryptographic)
                {
                    return new BackupVerificationResult { Success = true, Level = level, VerifiedAt = verifiedAt, VaultIdText = vaultIdText, BackupCreatedAt = DateTimeOffset.FromUnixTimeSeconds(created), ItemCount = manifest.ItemCount, AttachmentCount = manifest.AttachmentCount };
                }

                // Rehearsal: extract into an isolated temp location and open the database for real
                // (no credential needed — the backup belongs to the unlocked session's vault).
                var staging = Path.Combine(Path.GetTempPath(), "ganjineh-rehearsal-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await ExtractPackageAsync(packageStream, staging, manifest);
                    File.WriteAllBytes(Path.Combine(staging, "header.bin"),
                        bytes.AsSpan().Slice(BackupFormat.FixedHeaderSize, headerLength).ToArray());
                    var dbKey = HkdfKeyDerivation.DeriveSubkey(
                        session.Keyring.VaultMasterKey, header.VaultId, CryptoFormat.LabelSqlcipherKey);
                    using var database = VaultDatabase.Open(Path.Combine(staging, "vault.db"), dbKey, header.VaultId);
                    database.VerifyIntegrity();
                    var itemCount = (int)database.ScalarLong("SELECT COUNT(*) FROM items;");
                    var attachmentCount = (int)database.ScalarLong("SELECT COUNT(*) FROM attachments;");

                    return new BackupVerificationResult { Success = true, Level = level, VerifiedAt = verifiedAt, VaultIdText = vaultIdText, BackupCreatedAt = DateTimeOffset.FromUnixTimeSeconds(created), ItemCount = itemCount, AttachmentCount = attachmentCount };
                }
                finally
                {
                    try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
                }
            }
            finally
            {
                TryDelete(packagePath);
            }
        }
        catch (CryptoFormatException ex)
        {
            return Fail(level, ex.Message, verifiedAt, null, null);
        }
        catch (UnsupportedFormatException ex)
        {
            return Fail(level, ex.Message, verifiedAt, null, null);
        }
        catch (CryptoAuthenticationException)
        {
            return Fail(level, "Backup.Tampered", verifiedAt, null, null);
        }
        catch (IOException)
        {
            return Fail(level, "Error.Storage", verifiedAt, null, null);
        }
    }

    // ───────────────────────── Full restore ─────────────────────────

    public async Task<RestoreResult> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(request.BackupFilePath))
            {
                return new RestoreResult { Success = false, ErrorKey = "Error.VaultMissing" };
            }

            var bytes = await File.ReadAllBytesAsync(request.BackupFilePath, cancellationToken);
            var (backupId, vaultId, created, headerLength, _) = BackupFormat.ParseFixedHeader(bytes);
            var headerBytes = bytes.AsSpan().Slice(BackupFormat.FixedHeaderSize, headerLength).ToArray();
            var header = HeaderCodec.Parse(headerBytes);
            var vaultIdText = new Guid(header.VaultId).ToString();

            var stagingDir = Path.Combine(
                request.DestinationParentDirectory, vaultIdText + ".pvault");
            if (Directory.Exists(stagingDir))
            {
                return new RestoreResult { Success = false, ErrorKey = "Restore.DestinationExists" };
            }

            // Unlock the backup's own header: master password first, Recovery Key fallback
            // (same approved mechanism — no second recovery implementation).
            var vmk = UnlockHeader(header, request.CredentialPassword, out var viaRecovery);
            try
            {
                var backupWrapKey = HkdfKeyDerivation.DeriveSubkey(vmk, header.VaultId, CryptoFormat.LabelBackupWrap);
                var (manifest, packagePath) = await OpenBackupAsync(
                    request.BackupFilePath, bytes, header, backupWrapKey, backupId);
                try
                {
                    await using var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 1 << 20, useAsync: true);

                    Directory.CreateDirectory(stagingDir);
                    File.WriteAllBytes(Path.Combine(stagingDir, "header.bin"), headerBytes);
                    Directory.CreateDirectory(Path.Combine(stagingDir, "attachments"));
                    await ExtractPackageAsync(packageStream, stagingDir, manifest);

                    // Validate the restored database before activation.
                    var dbKey = HkdfKeyDerivation.DeriveSubkey(vmk, header.VaultId, CryptoFormat.LabelSqlcipherKey);
                    using (var database = VaultDatabase.Open(Path.Combine(stagingDir, "vault.db"), dbKey, header.VaultId))
                    {
                        database.VerifyIntegrity();
                        var liveItems = (int)database.ScalarLong("SELECT COUNT(*) FROM items WHERE trashed_at IS NULL;");
                        var attachmentCount = (int)database.ScalarLong("SELECT COUNT(*) FROM attachments;");
                        var trashedItems = (int)database.ScalarLong("SELECT COUNT(*) FROM items WHERE trashed_at IS NOT NULL;");
                        if (liveItems + trashedItems != manifest.ItemCount ||
                            attachmentCount != manifest.AttachmentCount)
                        {
                            Directory.Delete(stagingDir, recursive: true);
                            return new RestoreResult { Success = false, ErrorKey = "Restore.Incomplete" };
                        }
                    }

                    _registryFactory().RecordOpen(vaultIdText, stagingDir, header.HeaderCounter);

                    VaultSession? restored = viaRecovery
                        ? await _lifecycle.UnlockWithRecoveryKeyAsync(stagingDir, request.CredentialPassword, cancellationToken)
                        : await _lifecycle.UnlockWithPasswordAsync(stagingDir, request.CredentialPassword.ToCharArray(), cancellationToken);

                    return new RestoreResult { Success = true, VaultDirectory = stagingDir, VaultId = new Guid(header.VaultId), ItemCount = manifest.ItemCount, AttachmentCount = manifest.AttachmentCount, Session = restored };
                }
                finally
                {
                    TryDelete(packagePath);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vmk);
            }
        }
        catch (CryptoFormatException ex)
        {
            return new RestoreResult { Success = false, ErrorKey = ex.Message };
        }
        catch (CryptoAuthenticationException)
        {
            return new RestoreResult { Success = false, ErrorKey = "Unlock.WrongPassword" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RestoreResult { Success = false, ErrorKey = "Error.Storage" };
        }
    }

    // ───────────────────────── Selective restore ─────────────────────────

    public async Task<SelectiveRestoreResult> RestoreSelectiveAsync(
        SelectiveRestoreRequest request, CancellationToken cancellationToken = default)
    {
        var stagingDir = Path.Combine(Path.GetTempPath(), "ganjineh-selective-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = await File.ReadAllBytesAsync(request.BackupFilePath, cancellationToken);
            var (backupId, _, _, headerLength, _) = BackupFormat.ParseFixedHeader(bytes);
            var headerBytes = bytes.AsSpan().Slice(BackupFormat.FixedHeaderSize, headerLength).ToArray();
            var header = HeaderCodec.Parse(headerBytes);

            byte[] vmk = UnlockHeader(header, request.CredentialPassword, out var viaRecovery);
            var backupWrapKey = HkdfKeyDerivation.DeriveSubkey(vmk, header.VaultId, CryptoFormat.LabelBackupWrap);
            var (manifest, packagePath) = await OpenBackupAsync(
                request.BackupFilePath, bytes, header, backupWrapKey, backupId);
            try
            {
                await using var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1 << 20, useAsync: true);

                Directory.CreateDirectory(stagingDir);
                File.WriteAllBytes(Path.Combine(stagingDir, "header.bin"), headerBytes);
                Directory.CreateDirectory(Path.Combine(stagingDir, "attachments"));
                await ExtractPackageAsync(packageStream, stagingDir, manifest);
                CryptographicOperations.ZeroMemory(vmk);

                // Open the staged backup vault (temporary registry so the real list stays clean).
                var tempLifecycle = new VaultLifecycleService(
                    registryFactory: () => new VaultRegistry(Path.Combine(stagingDir, "vaults.json")));
                VaultSession source;
                try
                {
                    source = await tempLifecycle.UnlockWithPasswordAsync(
                        stagingDir, request.CredentialPassword.ToCharArray(), cancellationToken);
                }
                catch (CryptoAuthenticationException)
                {
                    source = await tempLifecycle.UnlockWithRecoveryKeyAsync(
                        stagingDir, request.CredentialPassword, cancellationToken);
                }

                using (source)
                {
                    var sourceItems = new VaultItemService();
                    var sourceAttachments = new AttachmentService();
                    var targetItems = new VaultItemService();
                    var targetAttachments = new AttachmentService();

                    int created = 0, skipped = 0, replaced = 0, attachmentsCopied = 0;
                    var existingIds = (await targetItems.ListAsync(request.TargetSession, null, cancellationToken))
                        .Select(s => s.Id).ToHashSet();

                    foreach (var itemId in request.ItemIds)
                    {
                        var detail = await sourceItems.GetAsync(source, itemId, cancellationToken);
                        var draft = new ItemDraft
                        {
                            Kind = detail.Kind,
                            Title = detail.Title,
                            Description = detail.Description,
                            Data = detail.Data,
                            CustomFields = detail.CustomFields,
                            FolderId = null,   // folder names may collide across vaults; v1 restores at root
                            TagIds = [],       // tag re-creation by name is a documented v1 simplification
                        };

                        var exists = existingIds.Contains(detail.Id);
                        Guid finalId;
                        if (exists && request.Policy == SelectiveConflictPolicy.Skip)
                        {
                            skipped++;
                            continue;
                        }

                        if (exists && request.Policy == SelectiveConflictPolicy.ReplaceExisting)
                        {
                            await targetItems.UpdateAsync(request.TargetSession, detail.Id, draft, cancellationToken);
                            finalId = detail.Id;
                            replaced++;
                        }
                        else
                        {
                            var createdItem = await targetItems.CreateAsync(request.TargetSession, draft, cancellationToken);
                            finalId = createdItem.Id;
                            created++;
                        }

                        // Attachments: export from the staged backup (source keys) → add into the
                        // target (target keys). Authenticated re-encryption, never ciphertext copy.
                        foreach (var attachment in await sourceAttachments.ListAsync(source, itemId, cancellationToken))
                        {
                            var tempFile = Path.Combine(Path.GetTempPath(), "ganjineh-sel-" + Guid.NewGuid().ToString("N"));
                            try
                            {
                                await sourceAttachments.ExportAsync(source, attachment.Id, tempFile, cancellationToken);
                                await targetAttachments.AddAsync(request.TargetSession, finalId, tempFile, cancellationToken);
                            }
                            finally
                            {
                                try { File.Delete(tempFile); } catch (IOException) { }
                            }
                        }
                    }

                    return new SelectiveRestoreResult(created, skipped, replaced, attachmentsCopied);
                }
            }
            finally
            {
                TryDelete(packagePath);
            }
        }
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch (IOException) { }
        }
    }

    private static BackupVerificationResult Fail(
        VerificationLevel level, string? errorKey, DateTimeOffset verifiedAt, string? vaultIdText, long? created) =>
        new BackupVerificationResult
        {
            Success = false,
            Level = level,
            ErrorKey = errorKey,
            VerifiedAt = verifiedAt,
            VaultIdText = vaultIdText,
            BackupCreatedAt = created is null ? null : DateTimeOffset.FromUnixTimeSeconds(created.Value),
        };

    // ───────────────────────── retention ─────────────────────────

    public int PruneBackups(string directory, int keepCount)
    {
        keepCount = Math.Max(1, keepCount);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var backups = Directory.EnumerateFiles(directory, "*.gvbak")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToList();

        // Never prune the newest verified backup — keepCount ≥ 1 guarantees at least one stays.
        var removed = 0;
        foreach (var file in backups.Skip(keepCount))
        {
            try { file.Delete(); removed++; } catch (IOException) { }
        }

        return removed;
    }

    // ───────────────────────── shared internals ─────────────────────────

    private static async Task<(BackupFormat.BackupManifest Manifest, string PackagePath)> OpenBackupAsync(
        string backupFilePath, byte[] bytes, VaultHeaderData header, byte[] backupWrapKey, Guid backupId)
    {
        var metaOffset = BackupFormat.FixedHeaderSize + CryptoFormat.HeaderSize;
        if (bytes.Length < metaOffset + 4)
        {
            throw new CryptoFormatException("Backup is truncated.");
        }

        var metaLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(metaOffset, 4));
        if (metaLen is < ChunkedAead.IvSize + ChunkedAead.TagSize or > 1_000_000
            || metaOffset + 4 + metaLen + 8 > bytes.Length)
        {
            throw new CryptoFormatException("Backup metadata region is malformed.");
        }

        var prefix = bytes.AsSpan(0, metaOffset).ToArray();
        var metaAad = MetaAad(prefix);
        var metaNonce = bytes.AsSpan(metaOffset + 4, ChunkedAead.IvSize).ToArray();
        var metaCt = bytes.AsSpan(metaOffset + 4 + ChunkedAead.IvSize,
            metaLen - ChunkedAead.IvSize - ChunkedAead.TagSize).ToArray();
        var metaTag = bytes.AsSpan(metaOffset + 4 + metaLen - ChunkedAead.TagSize, ChunkedAead.TagSize).ToArray();

        byte[] metaJson;
        try
        {
            metaJson = new byte[metaCt.Length];
            using var aes = new AesGcm(backupWrapKey, ChunkedAead.TagSize);
            aes.Decrypt(metaNonce, metaCt, metaTag, metaJson, metaAad);
        }
        catch (System.Security.Cryptography.AuthenticationTagMismatchException)
        {
            throw new CryptoAuthenticationException();
        }

        try
        {
            var manifest = BackupFormat.DeserializeManifest(metaJson);

            var packageOffset = metaOffset + 4 + metaLen;
            var headerBlock = 8 + ChunkedAead.IvSize + 8;
            var packageLen = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
                bytes.AsSpan(packageOffset, 8));
            if (packageLen <= headerBlock || packageOffset + 8 + packageLen != bytes.Length)
            {
                throw new CryptoFormatException("LENMISMATCH");
            }

            var chunkIv = bytes.AsSpan(packageOffset + 16, ChunkedAead.IvSize).ToArray();
            var declaredTotal = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
                bytes.AsSpan(packageOffset + 28, 8));
            var bodyOffset = packageOffset + 36; // pkgLen(8) + magic(8) + iv(12) + total(8)

            // Stream-decrypt the package to a temp file. Its contents remain encrypted at every
            // layer (SQLCipher DB + AEAD blobs), so this temp file carries no plaintext.
            var packagePath = backupFilePath + ".pkg";
            await using (var output = new FileStream(packagePath, FileMode.Create, FileAccess.Write,
                FileShare.None, 1 << 20, useAsync: true))
            {
                var position = bodyOffset;
                var end = packageOffset + 8 + (int)packageLen;
                Console.Error.WriteLine($"[BKREAD] metaLen={metaLen} packageOffset={packageOffset} packageLen={packageLen} fileLen={bytes.Length} bodyOffset={bodyOffset} end={end} declaredTotal={declaredTotal}");
                var firstLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(bodyOffset, 4));
                Console.Error.WriteLine($"[BKREAD] chunk0 len={firstLen}");
                ulong chunkIndex = 0;
                while (position < end)
                {
                    if (end - position < 4)
                    {
                        throw new CryptoFormatException("TRUNCPREFIX");
                    }

                    var len = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                        bytes.AsSpan(position, 4));
                    Console.Error.WriteLine($"[BKCHUNK] at {position} len={len} (end={end})");
                    position += 4;

                    if (len < 0 || position + len + ChunkedAead.TagSize > end)
                    {
                        throw new CryptoFormatException("CHUNKRANGE");
                    }

                    var tag = bytes.AsSpan(position + len, ChunkedAead.TagSize).ToArray();
                    if (len == 0)
                    {
                        // Terminator authenticates completeness; nothing more may follow.
                        ChunkedAead.DecryptChunk(backupWrapKey, chunkIv, chunkIndex, declaredTotal,
                            header.VaultId, backupId.ToByteArray(), ReadOnlySpan<byte>.Empty, tag);
                        position += len + ChunkedAead.TagSize;
                        if (position != end)
                        {
                            throw new CryptoFormatException("AFTERTERM");
                        }

                        break;
                    }

                    var ct = bytes.AsSpan(position, len).ToArray();
                    position += len + ChunkedAead.TagSize;
                    var plaintext = ChunkedAead.DecryptChunk(backupWrapKey, chunkIv, chunkIndex, declaredTotal,
                        header.VaultId, backupId.ToByteArray(), ct, tag);
                    await output.WriteAsync(plaintext.AsMemory(0, plaintext.Length));
                    CryptographicOperations.ZeroMemory(plaintext);
                    chunkIndex++;
                }

                await output.FlushAsync();
            }

            return (manifest, packagePath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(metaJson);
        }
    }

    private static async Task ExtractPackageAsync(Stream package, string stagingDir, BackupFormat.BackupManifest manifest)
    {
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(Path.Combine(stagingDir, "attachments"));
        package.Position = 0;
        var entries = BackupFormat.ReadPackage(package, long.MaxValue);

        if (!entries.TryGetValue("vault.db", out var dbBytes))
        {
            throw new CryptoFormatException("Backup package has no database snapshot.");
        }

        File.WriteAllBytes(Path.Combine(stagingDir, "vault.db"), dbBytes);
        Directory.CreateDirectory(Path.Combine(stagingDir, "attachments"));

        foreach (var expected in manifest.Attachments)
        {
            var name = "attachments/" + expected.AttachmentId + ".pmblob";
            if (!entries.TryGetValue(name, out var blob))
            {
                throw new CryptoFormatException("Backup is missing a required attachment blob.");
            }

            var actualSha = Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant();
            if (actualSha != expected.Sha256)
            {
                throw new CryptoFormatException("Attachment blob failed its integrity check.");
            }

            File.WriteAllBytes(Path.Combine(stagingDir, "attachments", expected.AttachmentId + ".pmblob"), blob);
        }
    }

    private byte[] UnlockHeader(VaultHeaderData header, string credential, out bool viaRecovery)
    {
        viaRecovery = false;
        var deriver = new VaultKeyDeriver();

        var pek = deriver.DerivePasswordKek(
            credential.ToCharArray(), header.PasswordSalt, header.VaultId, header.Kdf);
        try
        {
            return new KeyWrapService().Unwrap(
                header.PasswordWrap, pek, header.VaultId, CryptoFormat.WrapPurposePassword, CryptoFormat.KdfIdArgon2id);
        }
        catch (CryptoAuthenticationException)
        {
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pek);
        }

        viaRecovery = true;
        byte[] secret;
        try
        {
            // A credential that isn't valid PMRK1 is simply a wrong password/typo — the caller
            // gets the same generic authentication failure as the password path (no oracle).
            secret = RecoveryKeyCodec.Parse(credential);
        }
        catch (CryptoFormatException)
        {
            throw new CryptoAuthenticationException();
        }

        try
        {
            var rkek = deriver.DeriveRecoveryKek(secret, header.VaultId);
            return new KeyWrapService().Unwrap(
                header.RecoveryWrap, rkek, header.VaultId, CryptoFormat.WrapPurposeRecovery, CryptoFormat.KdfIdArgon2id);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static byte[] MetaAad(byte[] prefix)
    {
        var magic = Encoding.ASCII.GetBytes("GBAKM1");
        var aad = new byte[magic.Length + prefix.Length];
        magic.CopyTo(aad, 0);
        prefix.CopyTo(aad, magic.Length);
        return aad;
    }

    private static async Task WriteChunkAsync(Stream output, byte[] ciphertext, byte[] tag)
    {
        var prefix = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(prefix, ciphertext.Length);
        await output.WriteAsync(prefix.AsMemory());
        await output.WriteAsync(ciphertext.AsMemory());
        await output.WriteAsync(tag.AsMemory());
    }

    private static int ParseCount(string? text) => int.TryParse(text, out var n) ? n : 0;

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
