namespace PasswordManager.Cryptography.Errors;

/// <summary>Base type for all Ganjineh crypto-layer failures.</summary>
public abstract class CryptoException(string message = "") : Exception(message);

/// <summary>Authentication failed: wrong credential or tampered data. Deliberately indistinguishable.</summary>
public sealed class CryptoAuthenticationException() : CryptoException;

/// <summary>Malformed envelope/header structure (bad magic, version, lengths, reserved bytes).</summary>
public sealed class CryptoFormatException(string message) : CryptoException(message);

/// <summary>The artifact declares a format this engine does not support (e.g. newer header version).</summary>
public sealed class UnsupportedFormatException(string message) : CryptoException(message);

/// <summary>KDF parameters are missing, below the security floor, or beyond platform limits.</summary>
public sealed class InvalidKdfParametersException(string message) : CryptoException(message);
