namespace Ganjineh.Persistence.IO;

/// <summary>
/// Fault-injection points used by crash-safety tests. Production code always passes None.
/// </summary>
public enum AtomicFaultPoint
{
    None,
    AfterTempWrite,     // crash between durable temp write and the replace
    BeforeReplace,      // crash after temp fsync, before File.Replace
}

/// <summary>
/// The only sanctioned write path for security metadata: write sibling temp file → flush to disk
/// → File.Replace (atomic on the same NTFS volume) → keep .bak as the previous generation.
/// An interruption at any point leaves either the original or the new file, never a torn mix.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> contents, AtomicFaultPoint faultPoint = AtomicFaultPoint.None)
    {
        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(contents);
            stream.Flush(flushToDisk: true);
        }

        if (faultPoint == AtomicFaultPoint.AfterTempWrite)
        {
            throw new IOException($"Simulated crash after temp write: {path}");
        }

        if (faultPoint == AtomicFaultPoint.BeforeReplace)
        {
            throw new IOException($"Simulated crash before replace: {path}");
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    /// <summary>Removes a stale temp file left by an interrupted write (called on vault open).</summary>
    public static void CleanupTempFiles(string path)
    {
        try { File.Delete(path + ".tmp"); } catch (IOException) { }
        try { File.Delete(path + ".tmp" + "-wal"); } catch (IOException) { }
    }
}
