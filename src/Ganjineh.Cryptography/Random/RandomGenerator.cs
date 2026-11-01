using System.Security.Cryptography;

namespace PasswordManager.Cryptography.Random;

/// <summary>All secret randomness flows through this single seam (testability + audit).</summary>
public interface IRandomGenerator
{
    void Fill(Span<byte> buffer);
    byte[] GetBytes(int length);
}

public sealed class RandomGenerator : IRandomGenerator
{
    public static RandomGenerator Shared { get; } = new();

    public void Fill(Span<byte> buffer) => RandomNumberGenerator.Fill(buffer);

    public byte[] GetBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var buffer = new byte[length];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }
}
