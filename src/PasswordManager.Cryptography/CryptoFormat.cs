namespace PasswordManager.Cryptography;

/// <summary>
/// Wire-format constants for every Ganjineh crypto structure (cryptographic-specification.md).
/// Change any value here only together with a format-version bump and an ADR.
/// </summary>
public static class CryptoFormat
{
    public const string DomainPrefix = "ganjineh.v1";

    // Vault header ("GPVLT" + three zero bytes).
    public const int HeaderSize = 512;
    public const int HeaderMacOffset = 0x100;           // bytes 0x000–0x0FF are the authenticated span
    public const ushort HeaderFormatVersion = 1;
    public const ushort MinReaderVersion = 1;

    // Item/attachment/meta envelopes (crypto-specification.md §7).
    public const int EnvelopeMagicLength = 6;
    public const int EnvelopeHeaderLength = 23;         // magic(6) version(1) aead(2) domain(2) nonce(12)
    public const ulong MaxEnvelopePlaintextLength = 64 * 1024 * 1024;

    // Sizes in bytes.
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int SaltSize = 16;
    public const int VaultIdSize = 16;
    public const int RecoverySecretSize = 32;

    // Algorithm identifiers (little-endian u16 on the wire).
    public const ushort KdfIdArgon2id = 0x0001;
    public const ushort AeadIdAes256Gcm = 0x0001;
    public const ushort HashIdSha256 = 0x0001;

    // HKDF purpose labels; info = DomainPrefix ‖ 0x00 ‖ label (crypto-specification.md §3).
    public const string LabelPasswordKek = "password-kek";
    public const string LabelRecoveryKek = "recovery-kek";
    public const string LabelSqlcipherKey = "sqlcipher-key";
    public const string LabelItemEncryption = "item-encryption";
    public const string LabelAttachmentEncryption = "attachment-encryption";
    public const string LabelBackupWrap = "backup-wrap";
    public const string LabelHeaderMac = "header-mac";

    // Wrap-record AAD magic (vault-file-format.md §6, Revision 1).
    public const string WrapAadMagic = "GPWRP1";

    public const ushort WrapPurposePassword = 0x01;
    public const ushort WrapPurposeRecovery = 0x02;
}
