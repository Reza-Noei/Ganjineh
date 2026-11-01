using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ganjineh.Application.Credentials;
using Ganjineh.Application.Items;
using Ganjineh.Application.Vaults;
using Ganjineh.Persistence.Items;

namespace Ganjineh.Application.Import;

/// <summary>
/// Browser-CSV import: parse → validate → duplicate-match against the active vault → write
/// through the existing <see cref="IVaultItemService"/> (envelope-encrypted, versioned). The
/// service never writes plaintext to disk, never logs values, and holds passwords only inside
/// the session's secret map for the duration of the import (import-security.md).
/// </summary>
public interface IBrowserImportService
{
    /// <summary>Parses and validates the file, then duplicate-matches its rows against the live
    /// logins of the given unlocked session. The returned session owns the password material and
    /// must be disposed when the wizard closes.</summary>
    BrowserImportSession Parse(VaultSession session, ImportBrowser browser, string filePath, ImportFileLimits? limits = null, CancellationToken cancellationToken = default);

    /// <summary>Executes one row plan against the session. Each record is written in its own
    /// transaction; on cancel/lock the completed prefix stays and the result reports it.</summary>
    Task<ImportRunResult> ExecuteAsync(VaultSession session, BrowserImportSession data, IReadOnlyList<ImportRowPlan> plans, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class BrowserImportService(IVaultItemService? items = null) : IBrowserImportService
{
    private readonly IVaultItemService _items = items ?? new VaultItemService();

    // Same caps as the credential model — enforced at parse time so oversized rows are rejected
    // with a row number instead of failing mid-write.
    private const int MaxTitle = CredentialValidation.MaxTitle;
    private const int MaxUsername = CredentialValidation.MaxUsername;
    private const int MaxPassword = CredentialValidation.MaxPassword;
    private const int MaxUrl = CredentialValidation.MaxWebsiteUrl;
    private const int MaxNote = CredentialValidation.MaxNotes;

    public BrowserImportSession Parse(VaultSession session, ImportBrowser browser, string filePath, ImportFileLimits? limits = null, CancellationToken cancellationToken = default)
    {
        AssertUnlocked(session);
        var parsed = ParseFile(browser, filePath, limits, cancellationToken);
        MatchDuplicates(session, parsed, cancellationToken);
        return parsed;
    }

    /// <summary>Pure file parse (no vault involvement): read → map → validate. Unit-testable
    /// without a vault; the wizard then runs <see cref="MatchDuplicates"/> against the session.</summary>
    public BrowserImportSession ParseFile(ImportBrowser browser, string filePath, ImportFileLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        limits ??= new ImportFileLimits();

        var rows = BrowserCsvReader.ReadRows(filePath, limits, cancellationToken);
        if (rows.Count == 0)
        {
            throw new ImportFormatException("Import.ErrorNoRecords");
        }

        var headers = rows[0];
        if (!BrowserCsvSchema.Matches(browser, headers))
        {
            // Distinguish "right file, wrong wizard entry" from "not a browser export at all".
            var detected = BrowserCsvSchema.Detect(headers);
            if (detected is { } other)
            {
                throw new ImportSchemaException("Import.ErrorWrongBrowser", other.ToString());
            }

            throw ImportFormatException.UnknownFormat();
        }

        var map = BrowserCsvSchema.MapColumns(browser, headers);
        var unsupported = BrowserCsvSchema.UnmappedHeaders(browser, headers);

        var preview = new List<ImportPreviewRow>();
        var secrets = new Dictionary<int, ImportedSecretRow>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var emptyCount = 0;
        var malformedCount = 0;

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = rows[rowIndex];
            var rowNumber = rowIndex + 1;

            // A row whose column count disagrees with the header is malformed — counted and
            // skipped, never partially mapped (§6: reject or safely handle malformed inputs).
            if (fields.Length != headers.Length)
            {
                malformedCount++;
                continue;
            }

            var secret = ExtractSecretRow(browser, fields, map, rowNumber);
            var (status, reasonKey) = Validate(secret);
            if (status is ImportRecordStatus.Empty)
            {
                emptyCount++;
                continue;
            }

            var withinFile = !seenKeys.Add(
                DuplicateKey.OriginKey(secret.Url) + "\u001f" + DuplicateKey.UsernameKey(secret.Username));

            preview.Add(new ImportPreviewRow
            {
                RowNumber = rowNumber,
                Title = secret.Title ?? string.Empty,
                Username = secret.Username,
                OriginDisplay = DuplicateKey.OriginDisplay(secret.Url),
                SourceLabelKey = browser == ImportBrowser.Chrome ? "Import.SourceChrome" : "Import.SourceFirefox",
                Status = status,
                InvalidReasonKey = reasonKey,
                IsDuplicateWithinFile = withinFile,
            });
            secrets[rowNumber] = secret;
        }

        if (preview.Count == 0)
        {
            throw new ImportFormatException("Import.ErrorNoRecords");
        }

        var result = new BrowserImportSession
        {
            Browser = browser,
            SourceFilePath = Path.GetFullPath(filePath),
            UnsupportedColumns = unsupported,
            EmptyRowCount = emptyCount,
            MalformedRowCount = malformedCount,
        };
        result.AttachSecrets(secrets);
        result.SetRows(preview.ToArray());
        return result;
    }

    /// <summary>
    /// Duplicate match against the CURRENT vault only (§8) — never across locked vaults.
    /// Overwrites the session's rows with duplicate annotations; idempotent.
    /// </summary>
    public void MatchDuplicates(VaultSession session, BrowserImportSession data, CancellationToken cancellationToken = default)
    {
        AssertUnlocked(session);
        ArgumentNullException.ThrowIfNull(data);
        var existing = LoadExistingLogins(session, cancellationToken);
        var secrets = data.Secrets;
        data.SetRows(MarkDuplicates(data.Rows, secrets, existing));
    }

    public async Task<ImportRunResult> ExecuteAsync(VaultSession session, BrowserImportSession data, IReadOnlyList<ImportRowPlan> plans, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        AssertUnlocked(session);
        ArgumentNullException.ThrowIfNull(data);

        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var stoppedEarly = false;
        Dictionary<int, string> failures = [];
        Dictionary<int, Guid> written = [];
        var processed = 0;

        foreach (var plan in plans)
        {
            if (!data.Secrets.TryGetValue(plan.RowNumber, out var secret))
            {
                skipped++;
            }
            else
            {
                try
                {
                    // Revalidate the session before every write (§9): a lock or vault switch
                    // between records throws here and ends the run with accurate counts.
                    AssertUnlocked(session);
                    cancellationToken.ThrowIfCancellationRequested();
                    switch (plan.Action)
                    {
                        case ImportRowAction.Skip:
                            skipped++;
                            break;

                        case ImportRowAction.Import or ImportRowAction.CreateCopy:
                            var created = await _items.CreateAsync(session, BuildCreateDraft(data.Browser, secret), cancellationToken);
                            imported++;
                            written[plan.RowNumber] = created.Id;
                            break;

                        case ImportRowAction.UpdateExisting when plan.ExistingItemId is { } targetId:
                            var outcome = await UpdateExistingAsync(session, data.Browser, secret, targetId, cancellationToken);
                            if (outcome is { } writtenId)
                            {
                                updated++;
                                written[plan.RowNumber] = writtenId;
                            }
                            else
                            {
                                skipped++;
                            }

                            break;

                        case ImportRowAction.UpdateExisting:
                            skipped++;
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // The completed prefix persists (per-record transactions); surface exact counts.
                    stoppedEarly = true;
                    break;
                }
                catch (StaleSessionException)
                {
                    stoppedEarly = true;
                    break;
                }
                catch (KeyNotFoundException)
                {
                    // Target vanished between duplicate-match and write (e.g. purged): skip.
                    skipped++;
                }
                catch (ValidationException ex)
                {
                    failed++;
                    failures[plan.RowNumber] = "Validation." + ex.Message;
                }
            }

            processed++;
            progress?.Report(new ImportProgress(processed, plans.Count));
        }

        return new ImportRunResult
        {
            Imported = imported,
            Updated = updated,
            Skipped = skipped,
            Failed = failed,
            Total = plans.Count,
            FailureReasons = failures,
            WrittenItems = written,
            StoppedEarly = stoppedEarly,
        };
    }

    // ── parse helpers ──

    private static ImportedSecretRow ExtractSecretRow(ImportBrowser browser, string[] fields, Dictionary<BrowserField, int> map, int rowNumber)
    {
        string? Value(BrowserField field) => map.TryGetValue(field, out var index) && index >= 0 && index < fields.Length
            ? NullIfEmpty(fields[index])
            : null;

        string? url = Value(BrowserField.Url)?.Trim();
        // guid/timestamps are metadata: trim tolerant of export whitespace. (Passwords, usernames
        // and notes are taken verbatim — spaces in credentials are legitimate.)
        var guid = browser == ImportBrowser.Firefox ? Value(BrowserField.Guid)?.Trim() : null;
        long? changedUnix = null;
        if (browser == ImportBrowser.Firefox
            && Value(BrowserField.TimePasswordChanged) is { } prtime
            && long.TryParse(prtime, out var micros) && micros > 0)
        {
            // Firefox PRTime is microseconds since the Unix epoch; carried only when the column
            // actually held a number — nothing is invented for missing fields (§5).
            changedUnix = micros / 1_000_000;
        }

        return new ImportedSecretRow
        {
            RowNumber = rowNumber,
            Title = DeriveTitle(Value(BrowserField.Title), url, Value(BrowserField.Username), rowNumber),
            Username = Value(BrowserField.Username),
            Password = Value(BrowserField.Password),
            Url = url,
            Note = Value(BrowserField.Note),
            SourceRecordId = guid ?? rowNumber.ToString(CultureInfo.InvariantCulture),
            SourceTimePasswordChanged = changedUnix,
        };
    }

    /// <summary>The domain requires a title; browsers do not export one universally. Derivation
    /// order: exported name → URL host → username → neutral label. No other field is invented.</summary>
    private static string DeriveTitle(string? name, string? url, string? username, int rowNumber)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IdnHost.Length > 0)
            {
                return uri.IdnHost;
            }

            return url;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            return username.Trim();
        }

        return "Imported " + rowNumber.ToString(CultureInfo.InvariantCulture);
    }

    private static (ImportRecordStatus Status, string? ReasonKey) Validate(ImportedSecretRow row)
    {
        if (string.IsNullOrEmpty(row.Username) && string.IsNullOrEmpty(row.Password) && string.IsNullOrEmpty(row.Url))
        {
            return (ImportRecordStatus.Empty, null);
        }

        if (row.Title is null || row.Title.Length == 0 || row.Title.Length > MaxTitle)
        {
            return (ImportRecordStatus.Invalid, "Import.RowInvalidTitle");
        }

        if ((row.Username?.Length ?? 0) > MaxUsername)
        {
            return (ImportRecordStatus.Invalid, "Validation.UsernameTooLong");
        }

        if ((row.Password?.Length ?? 0) > MaxPassword)
        {
            return (ImportRecordStatus.Invalid, "Validation.PasswordTooLong");
        }

        if ((row.Note?.Length ?? 0) > MaxNote)
        {
            return (ImportRecordStatus.Invalid, "Import.RowInvalidNote");
        }

        if (row.Url is not null && !IsAcceptableUrl(row.Url))
        {
            return (ImportRecordStatus.Invalid, "Import.RowInvalidUrl");
        }

        return (ImportRecordStatus.Ready, null);
    }

    /// <summary>http(s) absolute URLs and bare hosts ("example.com", optionally with path) are
    /// accepted; everything else (schemes like javascript:, control characters) is rejected.
    /// Values are stored as-is — never interpreted as formulas, scripts or commands.</summary>
    private static bool IsAcceptableUrl(string url)
    {
        if (url.Length > MaxUrl)
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https";
        }

        // Bare-host form: no scheme, must look like host[/path] with no spaces or control chars.
        if (url.AsSpan().IndexOfAny([' ', '\t', '\r', '\n', '"', '\'', '<', '>']) >= 0)
        {
            return false;
        }

        return Uri.TryCreate("https://" + url, UriKind.Absolute, out var coerced)
            && coerced.HostNameType is UriHostNameType.Dns
            && coerced.IdnHost.Contains('.');
    }

    private static ImportPreviewRow[] MarkDuplicates(
        IReadOnlyList<ImportPreviewRow> preview,
        Dictionary<int, ImportedSecretRow> secrets,
        IReadOnlyList<ExistingLogin> existing)
    {
        var byKey = new Dictionary<string, List<ExistingLogin>>(StringComparer.Ordinal);
        foreach (var login in existing)
        {
            var key = login.OriginKey + "\u001f" + login.UsernameKey;
            if (!byKey.TryGetValue(key, out var list))
            {
                byKey[key] = list = [];
            }

            list.Add(login);
        }

        var marked = new ImportPreviewRow[preview.Count];
        for (var i = 0; i < preview.Count; i++)
        {
            var row = preview[i];
            if (row.Status is not ImportRecordStatus.Ready)
            {
                marked[i] = row;
                continue;
            }

            var secret = secrets[row.RowNumber];
            var key = DuplicateKey.OriginKey(secret.Url) + "\u001f" + DuplicateKey.UsernameKey(secret.Username);
            marked[i] = byKey.TryGetValue(key, out var matches) && matches.Count > 0
                ? row with { IsDuplicate = true, DuplicateOfTitle = matches[0].Title, ExistingItemId = matches[0].Id }
                : row;
        }

        return marked;
    }

    // ── write helpers ──

    private static ItemDraft BuildCreateDraft(ImportBrowser browser, ImportedSecretRow secret)
    {
        var data = new Dictionary<string, object?>
        {
            ["username"] = NullIfEmpty(secret.Username),
            ["password"] = NullIfEmpty(secret.Password),
            ["urls"] = string.IsNullOrWhiteSpace(secret.Url) ? new List<string>() : new List<string> { secret.Url },
            [ImportProvenance.SourceBrowser] = ImportProvenance.BrowserValue(browser),
            [ImportProvenance.ImportedAt] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            [ImportProvenance.SourceRecordId] = secret.SourceRecordId,
        };
        if (secret.SourceTimePasswordChanged is { } changed)
        {
            data["sourceTimePasswordChanged"] = changed.ToString(CultureInfo.InvariantCulture);
        }

        return new ItemDraft
        {
            Kind = ItemType.Login,
            Title = secret.Title ?? string.Empty,
            Description = NullIfEmpty(secret.Note),
            Data = data,
        };
    }

    private async Task<Guid?> UpdateExistingAsync(VaultSession session, ImportBrowser browser, ImportedSecretRow secret, Guid targetId, CancellationToken cancellationToken)
    {
        ItemDetail existing;
        try
        {
            existing = await _items.GetAsync(session, targetId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return null; // target gone → skip; never write a different item under its id
        }

        if (existing.Kind is not ItemType.Login)
        {
            return null;
        }

        var data = new Dictionary<string, object?>(existing.Data);
        // Merge, never clobber unrelated metadata: TOTP keys, custom data, favorite, folder,
        // tags and history all survive (UpdateAsync appends a version).
        if (!string.IsNullOrEmpty(secret.Username))
        {
            data["username"] = secret.Username;
        }

        // An empty password in the CSV never destroys the stored one — the update exists to
        // refresh credentials, not to blank them (documented policy).
        if (!string.IsNullOrEmpty(secret.Password))
        {
            data["password"] = secret.Password;
        }

        if (!string.IsNullOrWhiteSpace(secret.Url))
        {
            var urls = data.TryGetValue("urls", out var raw) && raw is List<string> list
                ? new List<string>(list)
                : [];
            if (!urls.Contains(secret.Url, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(secret.Url);
            }

            data["urls"] = urls;
        }

        data[ImportProvenance.SourceBrowser] = ImportProvenance.BrowserValue(browser);
        data[ImportProvenance.ImportedAt] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        data[ImportProvenance.SourceRecordId] = secret.SourceRecordId;
        if (secret.SourceTimePasswordChanged is { } changed)
        {
            data["sourceTimePasswordChanged"] = changed.ToString(CultureInfo.InvariantCulture);
        }

        var notes = existing.Description;
        if (!string.IsNullOrWhiteSpace(secret.Note))
        {
            // Existing notes are the user's content: the import note appends below, never overwrites.
            notes = string.IsNullOrWhiteSpace(notes)
                ? secret.Note
                : notes + "\n\n" + secret.Note;
        }

        var summary = await _items.UpdateAsync(session, targetId, new ItemDraft
        {
            Kind = ItemType.Login,
            Title = string.IsNullOrWhiteSpace(secret.Title) ? existing.Title : secret.Title,
            Description = notes,
            Data = data,
            CustomFields = existing.CustomFields,
            FolderId = existing.FolderId,
            TagIds = existing.TagIds,
        }, cancellationToken);
        return summary.Id;
    }

    // ── duplicate fingerprinting ──

    private readonly record struct ExistingLogin(Guid Id, string Title, string OriginKey, string UsernameKey);

    /// <summary>
    /// Enumerates live logins WITHOUT materializing passwords: only listing fields are read off
    /// the decrypted payload JSON, which is zeroized immediately. Single vault only — the
    /// session's own database (§8: never compare across locked vaults).
    /// </summary>
    private static List<ExistingLogin> LoadExistingLogins(VaultSession session, CancellationToken cancellationToken)
    {
        var repository = new ItemRepository(session.Database);
        var result = new List<ExistingLogin>();
        foreach (var row in repository.ListRows((int)ItemType.Login))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = repository.GetCurrentPayload(row.Id);
            if (envelope is null)
            {
                continue;
            }

            var json = ItemEncryption.DecryptJson(session.Keyring, session.Header.VaultId, row.Id, envelope);
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var username = ReadString(root, "username") ?? ReadString(root, "U");
                var url = ReadFirstUrl(root) ?? ReadString(root, "W");
                var title = root.TryGetProperty("Title", out var t) ? t.GetString() ?? string.Empty
                    : root.TryGetProperty("T", out var legacyT) ? legacyT.GetString() ?? string.Empty : string.Empty;
                result.Add(new ExistingLogin(
                    row.Id,
                    title,
                    DuplicateKey.OriginKey(url),
                    DuplicateKey.UsernameKey(username)));
            }
            finally
            {
                ZeroMemory(json);
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("Data", out var data) && data.ValueKind is JsonValueKind.Object
            && data.TryGetProperty(name, out var inner) && inner.ValueKind is JsonValueKind.String)
        {
            return inner.GetString();
        }

        if (root.TryGetProperty(name, out var direct) && direct.ValueKind is JsonValueKind.String)
        {
            return direct.GetString();
        }

        return null;
    }

    private static string? ReadFirstUrl(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object
            || !root.TryGetProperty("Data", out var data) || data.ValueKind is not JsonValueKind.Object
            || !data.TryGetProperty("urls", out var urls))
        {
            return null;
        }

        return urls.ValueKind switch
        {
            JsonValueKind.Array when urls.GetArrayLength() > 0 => urls[0].ValueKind is JsonValueKind.String ? urls[0].GetString() : null,
            JsonValueKind.String => urls.GetString(),
            _ => null,
        };
    }

    private static void ZeroMemory(byte[] buffer) => CryptographicOperations.ZeroMemory(buffer);

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static void AssertUnlocked(VaultSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Import operations require an unlocked vault session.");
        }
    }
}

/// <summary>
/// URL/username normalization for duplicate matching (§8). Origin keys keep scheme, full host
/// (IDN-folded, www-stripped — other subdomains preserved) and non-default port; paths and
/// queries are excluded from MATCHING only — stored URLs always keep them. Usernames fold
/// Arabic/Persian variants exactly like search (SearchNormalizer). Two accounts on one domain
/// with different usernames are never considered the same item.
/// </summary>
public static class DuplicateKey
{
    public static string OriginKey(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate("https://" + trimmed, UriKind.Absolute, out var coerced)
                || coerced.HostNameType is not UriHostNameType.Dns)
            {
                return string.Empty;
            }

            uri = coerced;
        }

        if (uri.Scheme is not ("http" or "https") || uri.IdnHost.Length == 0)
        {
            return string.Empty;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www."))
        {
            host = host[4..];
        }

        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return uri.Scheme + "://" + host + port;
    }

    public static string UsernameKey(string? username) => SearchNormalizer.Normalize(username).Trim();

    /// <summary>Display form for the preview table: host + path, query string stripped — URLs
    /// may carry tokens in their queries that have no business on screen.</summary>
    public static string OriginDisplay(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.Length <= 120 ? trimmed : trimmed[..120];
        }

        var display = uri.IsDefaultPort
            ? uri.IdnHost + uri.PathAndQuery
            : uri.IdnHost + ":" + uri.Port + uri.PathAndQuery;
        var queryStart = display.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            display = display[..queryStart];
        }

        return display.Length <= 120 ? display : display[..117] + "…";
    }
}
