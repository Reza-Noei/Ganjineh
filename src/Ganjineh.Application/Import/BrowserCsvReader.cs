using System.Globalization;
using System.Text;
using Sylvan.Data.Csv;

namespace Ganjineh.Application.Import;

/// <summary>
/// Bounded RFC 4180 row reader over an untrusted browser CSV file. The RFC 4180 mechanics come
/// from Sylvan.Data.Csv (maintained parser — import spec §6); this layer owns the safety
/// envelope: byte cap (detected, never silently truncated — unlike Backup's BoundedStream),
/// encoding detection (UTF-8 with/without BOM, UTF-16 BOMs defensively), record-count and
/// record/field size caps, and row-attributed malformed-row reporting. The file is opened
/// read-shared and never modified; no temporary copies are made.
/// </summary>
public static class BrowserCsvReader
{
    public static IReadOnlyList<string[]> ReadRows(string filePath, ImportFileLimits limits, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new ImportFormatException("Import.ErrorNotFound");
        }

        using var raw = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (raw.Length > limits.MaxFileBytes)
        {
            throw ImportFormatException.FileTooLarge();
        }

        var encoding = DetectEncoding(raw);
        // Cap applies after the BOM: a file that grew between the Length check and now still
        // cannot push more than MaxFileBytes through the parser.
        using var capped = new LengthCappedStream(raw, limits.MaxFileBytes);
        using var reader = new StreamReader(capped, encoding);
        return Parse(reader, limits, cancellationToken);
    }

    private static IReadOnlyList<string[]> Parse(TextReader reader, ImportFileLimits limits, CancellationToken cancellationToken)
    {
        var options = new CsvDataReaderOptions
        {
            HasHeaders = false,
            Delimiter = ',',
            Quote = '"',
            // Caps Sylvan's internal record buffer: an unterminated quote cannot make the parser
            // allocate without bound — it fails as a malformed record instead.
            MaxBufferSize = Math.Max(4096, limits.MaxRecordChars * 2),
        };

        var rows = new List<string[]>(capacity: 1024);
        try
        {
            using var csv = CsvDataReader.Create(reader, options);
            while (csv.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowNumber = rows.Count + 1;
                if (rows.Count >= limits.MaxRecords)
                {
                    throw ImportFormatException.TooManyRecords(limits.MaxRecords);
                }

                if (csv.FieldCount > 4096)
                {
                    throw ImportFormatException.Malformed(rowNumber);
                }

                var fields = new string[csv.FieldCount];
                for (var i = 0; i < csv.FieldCount; i++)
                {
                    var value = csv.IsDBNull(i) ? string.Empty : csv.GetString(i);
                    if (value.Length > limits.MaxFieldChars)
                    {
                        throw ImportFormatException.Malformed(rowNumber);
                    }

                    fields[i] = value;
                }

                rows.Add(fields);
            }
        }
        catch (CsvInvalidCharacterException)
        {
            // Sylvan's non-row-attributed parse failure (e.g. unterminated quote at EOF);
            // must precede the CsvFormatException catch — it derives from it.
            throw ImportFormatException.Malformed(rows.Count + 1);
        }
        catch (CsvFormatException ex)
        {
            throw ImportFormatException.Malformed(ex.RowNumber);
        }
        catch (DecoderFallbackException)
        {
            throw ImportFormatException.Encoding();
        }

        return rows;
    }

    /// <summary>UTF-8 (with or without BOM) is the documented browser export encoding; UTF-16
    /// BOMs are handled defensively. A BOM-less file is decoded as strict UTF-8 — invalid bytes
    /// reject the whole file rather than silently corrupting credentials (persian text safety).</summary>
    private static Encoding DetectEncoding(FileStream stream)
    {
        Span<byte> bom = stackalloc byte[3];
        var read = stream.Read(bom);
        stream.Seek(0, SeekOrigin.Begin);
        return read switch
        {
            >= 3 when bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF => new UTF8Encoding(false, true),
            >= 2 when bom[0] == 0xFF && bom[1] == 0xFE => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            >= 2 when bom[0] == 0xFE && bom[1] == 0xFF => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            _ => new UTF8Encoding(false, true),
        };
    }

    /// <summary>Throwing twin of Backup's BoundedStream: exceeding the cap fails the import
    /// instead of silently truncating credential rows.</summary>
    private sealed class LengthCappedStream(Stream inner, long maxBytes) : Stream
    {
        private long _consumed;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _consumed; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            _consumed += read;
            if (_consumed > maxBytes)
            {
                throw ImportFormatException.FileTooLarge();
            }

            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// Header-based (never positional) mapping between a CSV's columns and the browser export
/// schema. Header names are matched case-insensitively after trimming; the adapters are written
/// against the documented export formats and tolerate reordering, extra/unknown columns, and
/// missing optional columns (import spec §4/§5).
/// </summary>
public static class BrowserCsvSchema
{
    /// <summary>Columns every supported browser export must contain.</summary>
    public static readonly string[] Required = ["url", "username", "password"];

    /// <summary>Columns that only a Firefox export carries (disambiguates the two shared
    /// url/username/password vocabularies).</summary>
    private static readonly string[] FirefoxMarkers = ["guid", "httprealm", "formactionorigin", "timecreated"];

    /// <summary>Normalizes a raw header for matching: trim, BOM/ZWSP strip, lowercase.</summary>
    public static string NormalizeHeader(string raw) => raw.Trim().TrimStart('﻿', '‌', '‍').Trim().ToLowerInvariant();

    /// <summary>Detects which browser schema a header row matches, or null when unknown.</summary>
    public static ImportBrowser? Detect(IReadOnlyList<string> headers)
    {
        var normalized = headers.Select(NormalizeHeader).ToHashSet();
        if (!Required.All(normalized.Contains))
        {
            return null;
        }

        if (FirefoxMarkers.Any(normalized.Contains))
        {
            return ImportBrowser.Firefox;
        }

        return normalized.Contains("name") ? ImportBrowser.Chrome : null;
    }

    /// <summary>True when the header row satisfies the requested browser's schema.</summary>
    public static bool Matches(ImportBrowser browser, IReadOnlyList<string> headers)
    {
        var normalized = headers.Select(NormalizeHeader).ToHashSet();
        if (!Required.All(normalized.Contains))
        {
            return false;
        }

        return browser switch
        {
            ImportBrowser.Firefox => FirefoxMarkers.Any(normalized.Contains),
            ImportBrowser.Chrome => normalized.Contains("name") || !FirefoxMarkers.Any(normalized.Contains),
            _ => false,
        };
    }

    /// <summary>Resolves column indexes for a header row against a browser's field aliases.
    /// Duplicate headers resolve to the first occurrence; missing optional columns yield -1.</summary>
    public static Dictionary<BrowserField, int> MapColumns(ImportBrowser browser, IReadOnlyList<string> headers)
    {
        var aliases = browser switch
        {
            ImportBrowser.Chrome => new Dictionary<BrowserField, string[]>
            {
                [BrowserField.Title] = ["name"],
                [BrowserField.Url] = ["url", "website", "login_uri"],
                [BrowserField.Username] = ["username", "login_username"],
                [BrowserField.Password] = ["password", "login_password"],
                [BrowserField.Note] = ["note", "notes"],
            },
            ImportBrowser.Firefox => new Dictionary<BrowserField, string[]>
            {
                [BrowserField.Url] = ["url"],
                [BrowserField.Username] = ["username"],
                [BrowserField.Password] = ["password"],
                [BrowserField.Guid] = ["guid"],
                [BrowserField.TimePasswordChanged] = ["timepasswordchanged"],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(browser)),
        };

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);
            // First occurrence wins on duplicate headers — later ones are reported upstream
            // as unsupported columns so nothing is silently dropped (import spec §4).
            if (!seen.ContainsKey(normalized))
            {
                seen[normalized] = i;
            }
        }

        var map = new Dictionary<BrowserField, int>();
        foreach (var (field, names) in aliases)
        {
            map[field] = names.Select(n => seen.GetValueOrDefault(n, -1)).FirstOrDefault(i => i >= 0, -1);
        }

        return map;
    }

    /// <summary>Header names that no adapter mapped — surfaced to the user as "not imported"
    /// counts, never silently dropped.</summary>
    public static IReadOnlyList<string> UnmappedHeaders(ImportBrowser browser, IReadOnlyList<string> headers)
    {
        var mapped = MapColumns(browser, headers).Values.Where(i => i >= 0).ToHashSet();
        return headers
            .Select((name, index) => (name, index))
            .Where(h => !mapped.Contains(h.index))
            .Select(h => h.name.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>CSV columns the adapters consume.</summary>
public enum BrowserField
{
    Title = 0,
    Url = 1,
    Username = 2,
    Password = 3,
    Note = 4,
    Guid = 5,
    TimePasswordChanged = 6,
}
