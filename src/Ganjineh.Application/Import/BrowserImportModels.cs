namespace PasswordManager.Application.Import;

/// <summary>Browser CSV export sources supported by the wizard (Phase 9). CSV is only an import
/// transport format — parsed rows are mapped onto the existing Login item model and sealed by
/// the regular vault services; no browser-specific storage type exists.</summary>
public enum ImportBrowser
{
    Chrome = 1,
    Firefox = 2,
}

/// <summary>Per-row action chosen in the preview step. Duplicates default to Skip; non-duplicate
/// rows are always Import (there is nothing to conflict with).</summary>
public enum ImportRowAction
{
    Import = 0,
    Skip = 1,
    UpdateExisting = 2,
    CreateCopy = 3,
}

/// <summary>Hard parse bounds for untrusted browser CSV files (import-security.md). Mirrors the
/// Phase 6 file bounds (10 MB / 50 000 records) and adds per-field/per-record caps so a hostile
/// file cannot drive unbounded allocations.</summary>
public sealed record ImportFileLimits
{
    public const int DefaultMaxFileBytes = 10 * 1024 * 1024;
    public const int DefaultMaxRecords = 50_000;
    public const int DefaultMaxFieldChars = 8_192;
    public const int DefaultMaxRecordChars = 32_768;

    public int MaxFileBytes { get; init; } = DefaultMaxFileBytes;
    public int MaxRecords { get; init; } = DefaultMaxRecords;
    public int MaxFieldChars { get; init; } = DefaultMaxFieldChars;
    public int MaxRecordChars { get; init; } = DefaultMaxRecordChars;
}

/// <summary>Row-level validation outcome. Invalid rows stay visible in the preview (with a reason)
/// but cannot be selected for import. Empty rows (no url/username/password) are counted, not shown.</summary>
public enum ImportRecordStatus
{
    Ready = 0,
    Invalid = 1,
    Empty = 2,
}

/// <summary>
/// The UI-facing projection of one parsed CSV row. Deliberately carries NO password material —
/// the preview table binds to these records only. Passwords stay inside the owning
/// <see cref="BrowserImportSession"/> until the write step.
/// </summary>
public sealed record ImportPreviewRow
{
    public required int RowNumber { get; init; }
    public required string Title { get; init; }
    public string? Username { get; init; }

    /// <summary>Host (+ path when short) for display only; the query string is never shown —
    /// URLs may carry tokens in their query.</summary>
    public required string OriginDisplay { get; init; }

    /// <summary>"Import.SourceChrome" / "Import.SourceFirefox" resource key.</summary>
    public required string SourceLabelKey { get; init; }

    public required ImportRecordStatus Status { get; init; }

    /// <summary>Validation.* / Import.* resource key when Status is Invalid.</summary>
    public string? InvalidReasonKey { get; init; }

    public bool IsDuplicate { get; init; }

    /// <summary>Title of the existing vault item this row collides with (display help only).</summary>
    public string? DuplicateOfTitle { get; init; }

    /// <summary>Id of the matched existing item — resolved at parse time so the Update-Existing
    /// action writes to exactly the matched record. A non-sensitive vault item UUID.</summary>
    public Guid? ExistingItemId { get; init; }

    /// <summary>A later row in the same file repeats this row's origin+username.</summary>
    public bool IsDuplicateWithinFile { get; init; }
}

/// <summary>Import decision for one preview row, resolved by the wizard before execution.</summary>
public sealed record ImportRowPlan(int RowNumber, ImportRowAction Action, Guid? ExistingItemId);

/// <summary>Mutable holder for the secret-class fields of one CSV row. Lives only inside the
/// import session's private map; zeroizing managed strings is best-effort (secure-memory-
/// limitations.md) — the session clears its references on Dispose and relies on GC otherwise.</summary>
public sealed class ImportedSecretRow
{
    public required int RowNumber { get; init; }
    public string? Title { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Url { get; set; }
    public string? Note { get; set; }

    /// <summary>Firefox source guid (or the synthetic row number) — provenance only, never a
    /// vault item identity.</summary>
    public string? SourceRecordId { get; set; }

    /// <summary>timePasswordChanged as Unix seconds (Firefox exports microseconds) — provenance
    /// only; the item's own CreatedAt/UpdatedAt stay vault-owned.</summary>
    public long? SourceTimePasswordChanged { get; set; }
}

/// <summary>
/// One full parse: the safe preview rows, the secret rows held back from the UI, and the
/// file-level counters. Disposing the session drops all password references. No plaintext is
/// ever written to disk by the parser (import-security.md §4).
/// </summary>
public sealed class BrowserImportSession : IDisposable
{
    private Dictionary<int, ImportedSecretRow>? _secrets;

    public required ImportBrowser Browser { get; init; }

    /// <summary>Absolute path of the parsed file — used only to offer "open containing folder"
    /// after import (§13); it is never persisted into the vault.</summary>
    public required string SourceFilePath { get; init; }

    public required IReadOnlyList<string> UnsupportedColumns { get; init; }

    public int EmptyRowCount { get; init; }

    public int MalformedRowCount { get; init; }

    internal Dictionary<int, ImportedSecretRow> Secrets => _secrets ?? throw new ObjectDisposedException(nameof(BrowserImportSession));

    internal void AttachSecrets(Dictionary<int, ImportedSecretRow> secrets) => _secrets = secrets;

    internal void SetRows(IReadOnlyList<ImportPreviewRow> rows)
    {
        _rows = rows;
    }

    private IReadOnlyList<ImportPreviewRow> _rows = [];

    /// <summary>Preview rows with the latest duplicate annotations (replaced by MatchDuplicates).</summary>
    public IReadOnlyList<ImportPreviewRow> Rows => _rows;

    public void Dispose()
    {
        if (_secrets is null)
        {
            return;
        }

        foreach (var row in _secrets.Values)
        {
            // Managed string contents cannot be reliably zeroized; drop the references and let
            // the collector reclaim them (documented limitation, secure-memory-limitations.md).
            row.Title = null;
            row.Username = null;
            row.Password = null;
            row.Url = null;
            row.Note = null;
            row.SourceRecordId = null;
        }

        _secrets = null;
    }
}

/// <summary>File-level parse failure. <see cref="Detail"/> may carry a row number only — never
/// file content.</summary>
public sealed class ImportFormatException(string errorKey, string? detail = null) : Exception(errorKey)
{
    public string ErrorKey => Message;

    public string? Detail { get; } = detail;

    public static ImportFormatException FileTooLarge() => new("Import.ErrorFileTooLarge");

    public static ImportFormatException TooManyRecords(int max) => new("Import.ErrorTooManyRecords", max.ToString());

    public static ImportFormatException Encoding() => new("Import.ErrorEncoding");

    public static ImportFormatException Malformed(int row) => new("Import.ErrorMalformed", row.ToString());

    public static ImportFormatException UnknownFormat() => new("Import.ErrorUnknownFormat");
}

/// <summary>Thrown when the selected file's header set matches neither the chosen browser's
/// schema nor any known browser schema (wrong file / wrong wizard entry point).</summary>
public sealed class ImportSchemaException(string errorKey, string? detail = null) : Exception(errorKey)
{
    public string ErrorKey => Message;

    public string? Detail { get; } = detail;
}

/// <summary>Progress tick for the execute step (counts only — no content).</summary>
public sealed record ImportProgress(int Processed, int Total);

/// <summary>Final per-record outcome of one execution run. Each record is its own transaction, so
/// after a cancellation or session loss the counts below describe exactly which records landed —
/// a partially completed run is never reported as fully imported (§11).</summary>
public sealed record ImportRunResult
{
    public required int Imported { get; init; }
    public required int Updated { get; init; }
    public required int Skipped { get; init; }
    public required int Failed { get; init; }
    public required int Total { get; init; }

    /// <summary>Row number → error resource key for failed records (no content, no passwords).</summary>
    public required IReadOnlyDictionary<int, string> FailureReasons { get; init; }

    /// <summary>Created/updated item ids keyed by source row, for post-import navigation.</summary>
    public required IReadOnlyDictionary<int, Guid> WrittenItems { get; init; }

    /// <summary>True when the run stopped early (cancel or vault lock/switch). Completed records
    /// remain — per-record transactions.</summary>
    public bool StoppedEarly { get; init; }

    public int Processed => Imported + Updated + Skipped + Failed;
}

/// <summary>Non-sensitive provenance keys stored inside the Login payload Data dictionary
/// (§12). They ride inside the encrypted envelope; the plaintext values name only the source
/// browser, the import moment and the source record id — never the source file or its path.</summary>
public static class ImportProvenance
{
    public const string SourceBrowser = "sourceBrowser";
    public const string ImportedAt = "importedAt";
    public const string SourceRecordId = "sourceRecordId";

    public static string BrowserValue(ImportBrowser browser) => browser switch
    {
        ImportBrowser.Chrome => "chrome",
        ImportBrowser.Firefox => "firefox",
        _ => "unknown",
    };
}
