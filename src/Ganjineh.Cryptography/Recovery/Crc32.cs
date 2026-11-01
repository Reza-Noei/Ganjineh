namespace PasswordManager.Cryptography.Recovery;

/// <summary>
/// Standard CRC-32 (IEEE 802.3, reflected, poly 0xEDB88320). A transcription checksum, NOT a
/// cryptographic primitive — it only catches typing errors in the Recovery Key.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var i = 0; i < 256; i++)
        {
            var value = (uint)i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (0xEDB88320 ^ (value >> 1)) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc = Table[(int)((crc ^ b) & 0xFF)] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
