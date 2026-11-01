using System.Security.Cryptography;
using PasswordManager.Cryptography.Random;

namespace PasswordManager.Cryptography.Secrets;

/// <summary>
/// Disposable secret container. Zeroization on Dispose is best-effort in a garbage-collected
/// runtime (GC may have already relocated the buffer) — documented residual risk R-1 in
/// threat-model.md and docs/security/secure-memory-limitations.md.
/// </summary>
public sealed class KeyMaterial : IDisposable
{
    private byte[]? _buffer;

    private KeyMaterial(byte[] buffer) => _buffer = buffer;

    public static KeyMaterial FromBytes(ReadOnlySpan<byte> bytes)
    {
        var copy = new byte[bytes.Length];
        bytes.CopyTo(copy);
        return new KeyMaterial(copy);
    }

    public static KeyMaterial Random(IRandomGenerator rng, int length) => new(rng.GetBytes(length));

    /// <summary>UTF-8 encodes a password char buffer exactly as typed: no normalization, no trimming.</summary>
    public static KeyMaterial FromPasswordChars(ReadOnlySpan<char> passwordChars)
    {
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(passwordChars);
        var bytes = new byte[byteCount];
        System.Text.Encoding.UTF8.GetBytes(passwordChars, bytes);
        return new KeyMaterial(bytes);
    }

    public int Length => _buffer?.Length ?? 0;

    public ReadOnlySpan<byte> Span => _buffer is null
        ? throw new ObjectDisposedException(nameof(KeyMaterial))
        : _buffer;

    /// <summary>Copies the secret out (callers must zeroize the copy); never hands out the internal array.</summary>
    public byte[] ToArray() => Span.ToArray();

    public void Dispose()
    {
        if (_buffer is not null)
        {
            CryptographicOperations.ZeroMemory(_buffer.AsSpan());
            _buffer = null;
        }
    }
}
