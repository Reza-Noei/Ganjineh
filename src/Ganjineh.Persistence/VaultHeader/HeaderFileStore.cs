using Ganjineh.Cryptography;
using Ganjineh.Cryptography.Errors;
using Ganjineh.Cryptography.Kdf;
using Ganjineh.Cryptography.KeyWrapping;
using Ganjineh.Persistence.IO;

namespace Ganjineh.Persistence.VaultHeader;

/// <summary>Reads and writes header.bin through the atomic-write path; bumps the counter per rewrite.</summary>
public sealed class HeaderFileStore
{
    public VaultHeaderData Read(string headerPath)
    {
        var bytes = File.ReadAllBytes(headerPath);
        return HeaderCodec.Parse(bytes);
    }

    public byte[] ReadBytes(string headerPath) => File.ReadAllBytes(headerPath);

    /// <summary>
    /// Atomically replaces the header with an incremented counter. The new wraps must already be
    /// complete and verified by the caller — the previous header stays intact until the replace.
    /// The MAC covers bytes 0x000–0x0FF and is patched in before the durable write.
    /// </summary>
    public byte[] WriteUpdated(
        string headerPath,
        VaultHeaderData updated,
        ReadOnlySpan<byte> headerMacKey,
        AtomicFaultPoint faultPoint = AtomicFaultPoint.None)
    {
        ArgumentNullException.ThrowIfNull(updated);
        updated.HeaderCounter++;

        var bytes = HeaderCodec.Serialize(updated);
        var mac = HeaderCodec.ComputeHeaderMac(headerMacKey, bytes);
        mac.CopyTo(bytes, CryptoFormat.HeaderMacOffset);

        AtomicFile.WriteAllBytes(headerPath, bytes, faultPoint);
        return bytes;
    }

    public bool Exists(string headerPath) => File.Exists(headerPath);

    public static void ValidateOnOpen(VaultHeaderData header)
    {
        // Structural floors enforced at parse time; cryptographic floors here (ADR-003). Below-floor
        // parameters indicate a possible downgrade rollback and refuse to open.
        try
        {
            header.Kdf.Validate();
        }
        catch (InvalidKdfParametersException ex)
        {
            throw new CryptoFormatException(ex.Message);
        }
    }
}

