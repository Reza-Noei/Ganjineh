using NSec.Cryptography;
using Ganjineh.Cryptography.Errors;

namespace Ganjineh.Cryptography.Kdf;

/// <summary>Versioned Argon2id parameters (per vault, stored in the authenticated header).</summary>
public readonly record struct KdfParameters(long MemoryKiB, long Passes, int Parallelism)
{
    /// <summary>Hard floors: the app refuses to *create* a vault below these (ADR-003).</summary>
    public const long MinMemoryKiB = 16 * 1024;
    public const long MinPasses = 2;
    public const int MinParallelism = 1;

    /// <summary>Version of the parameters structure itself (future KDF upgrades bump this).</summary>
    public const ushort MetadataVersion = 1;

    /// <summary>
    /// Baseline per ADR-003 benchmark (2026-10-22, dev machine): 128 MiB/t=3/p=1 ≈ 172 ms median,
    /// projected ~0.35–0.7 s on min-spec hardware — inside the 0.3–1.0 s unlock band.
    /// Parallelism is fixed to 1 by the NSec/libsodium engine (see ADR-003 revision note).
    /// </summary>
    public static KdfParameters Baseline { get; } = new(128 * 1024, 3, 1);

    public void Validate()
    {
        if (MemoryKiB < MinMemoryKiB)
        {
            throw new InvalidKdfParametersException($"Argon2id memory below the security floor ({MemoryKiB} KiB).");
        }

        if (Passes < MinPasses)
        {
            throw new InvalidKdfParametersException($"Argon2id pass count below the security floor ({Passes}).");
        }

        if (Parallelism != 1)
        {
            // NSec 26 (libsodium crypto_pwhash) supports DegreeOfParallelism = 1 only; the metadata
            // field stays for future engines. See ADR-003 revision note.
            throw new InvalidKdfParametersException("Argon2id parallelism other than 1 is not supported by this engine.");
        }

        if (MemoryKiB > int.MaxValue / 1024)
        {
            throw new InvalidKdfParametersException("Argon2id memory parameter exceeds platform limits.");
        }
    }
}

/// <summary>Password-based key derivation via Argon2id (ADR-003). Passwords are raw UTF-8 bytes.</summary>
public interface IPasswordKdfService
{
    /// <summary>Derives 32 bytes from the password bytes and salt; parameters are validated first.</summary>
    byte[] Derive(ReadOnlySpan<byte> passwordBytes, ReadOnlySpan<byte> salt, KdfParameters parameters);
}

public sealed class Argon2idKdfService : IPasswordKdfService
{
    public byte[] Derive(ReadOnlySpan<byte> passwordBytes, ReadOnlySpan<byte> salt, KdfParameters parameters)
    {
        parameters.Validate();
        if (salt.Length != CryptoFormat.SaltSize)
        {
            throw new ArgumentException("Argon2id salt must be 128 bits.", nameof(salt));
        }

        var algorithm = new Argon2id(new Argon2Parameters
        {
            MemorySize = parameters.MemoryKiB,
            NumberOfPasses = parameters.Passes,
            DegreeOfParallelism = parameters.Parallelism,
        });

        var output = algorithm.DeriveBytes(passwordBytes, salt, CryptoFormat.KeySize);
        if (output.Length != CryptoFormat.KeySize)
        {
            throw new InvalidKdfParametersException("Argon2id derivation returned an unexpected length.");
        }

        return output;
    }
}
