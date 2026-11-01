using PasswordManager.Cryptography.Errors;

namespace PasswordManager.Persistence;

/// <summary>Vault file I/O failure (missing, unreadable, unwritable).</summary>
public sealed class StorageIoException(string message) : Exception(message);

/// <summary>SQLCipher provider initialization or keying failure (ADR-002 genuineness gate).</summary>
public sealed class CipherProviderException(string message) : Exception(message);

/// <summary>The database failed integrity validation.</summary>
public class DatabaseCorruptionException(string message) : Exception(message);

/// <summary>The database's stored vault identity does not match the authenticated header.</summary>
public sealed class VaultIdentityMismatchException(string message) : DatabaseCorruptionException(message);
