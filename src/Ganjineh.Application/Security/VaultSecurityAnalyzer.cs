using System.Text;
using System.Security.Cryptography;
using PasswordManager.Application.Credentials;
using PasswordManager.Application.Items;
using PasswordManager.Application.Vaults;
using PasswordManager.Persistence.Items;

namespace PasswordManager.Application.Security;

public enum FindingSeverity
{
    Informational = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public enum FindingKind
{
    WeakPassword,
    ReusedPassword,
    EmptyPassword,
    OldPassword,
    MissingAuthenticator,
    DuplicateAccount,
    ExpiringSecret,
}

/// <summary>A security finding. NEVER carries password plaintext (contract §8.1/§10).</summary>
public sealed record SecurityFinding(
    Guid ItemId,
    string ItemTitle,
    FindingKind Kind,
    FindingSeverity Severity,
    string DetailKey);

public sealed record VaultSecurityReport(
    int TotalLogins,
    IReadOnlyList<SecurityFinding> Findings,
    DateTimeOffset GeneratedAt)
{
    public int Count(FindingKind kind) => Findings.Count(f => f.Kind == kind);

    /// <summary>
    /// Documented aggregate score (0–100): 100 − Σ severity penalties, capped at 0.
    /// Critical −20, High −10, Medium −5, Informational −0. It is a coarse indicator of LOCAL
    /// vault hygiene only — not a measure of account security and not a guarantee.
    /// </summary>
    public int Score
    {
        get
        {
            var penalty = Findings.Sum(f => f.Severity switch
            {
                FindingSeverity.Critical => 20,
                FindingSeverity.High => 10,
                FindingSeverity.Medium => 5,
                _ => 0,
            });
            return Math.Max(0, 100 - penalty);
        }
    }
}

/// <summary>
/// Offline security analysis of the currently unlocked vault (contract §8/§10). All data is
/// decrypted in memory for the analysis only; the report carries no password material; findings
/// live in the workspace VM and are wiped with the session.
/// </summary>
public interface IVaultSecurityAnalyzer
{
    Task<VaultSecurityReport> AnalyzeAsync(VaultSession session, CancellationToken cancellationToken = default);
}

public sealed class VaultSecurityAnalyzer : IVaultSecurityAnalyzer
{
    public const int OldPasswordDays = 365;
    public const int ExpiringSoonDays = 30;

    private readonly IPasswordStrengthEstimator _estimator;
    private readonly IOtpApplicationService _otp;

    public VaultSecurityAnalyzer(IPasswordStrengthEstimator? estimator = null, IOtpApplicationService? otp = null)
    {
        _estimator = estimator ?? new ZxcvbnStrengthEstimator();
        _otp = otp ?? new OtpApplicationService();
    }

    public async Task<VaultSecurityReport> AnalyzeAsync(VaultSession session, CancellationToken cancellationToken = default)
    {
        if (session.State is not VaultSessionState.Unlocked)
        {
            throw new StaleSessionException("Security analysis requires an unlocked vault session.");
        }

        await Task.Yield();
        var repository = new ItemRepository(session.Database);
        var findings = new List<SecurityFinding>();

        var logins = new List<(ItemRow Row, string Title, string? Username, string? Password, bool HasTotp)>();
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
                using var document = System.Text.Json.JsonDocument.Parse(json);
                var root = document.RootElement;
                var title = root.TryGetProperty("Title", out var t) ? t.GetString() ?? string.Empty
                    : root.TryGetProperty("T", out var legacyT) ? legacyT.GetString() ?? string.Empty : string.Empty;
                var username = GetString(root, "username") ?? GetString(root, "U");
                var password = GetString(root, "password") ?? GetString(root, "P");
                var totpSecret = GetString(root, "totpSecret");
                logins.Add((row, title, username, password, !string.IsNullOrEmpty(totpSecret)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }

        // Reused passwords: exact match groups via an ephemeral in-memory hash set — nothing
        // persisted, wiped with the session (contract §8.2).
        var byPassword = new Dictionary<string, List<(Guid Id, string Title)>>(StringComparer.Ordinal);
        foreach (var (row, title, _, password, _) in logins)
        {
            if (string.IsNullOrEmpty(password))
            {
                continue;
            }

            var fingerprint = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password))); // ephemeral grouping only
            if (!byPassword.TryGetValue(fingerprint, out var group))
            {
                byPassword[fingerprint] = group = [];
            }

            group.Add((row.Id, title));
        }

        var reusedIds = byPassword.Values.Where(g => g.Count > 1)
            .SelectMany(g => g).Select(g => g.Id).ToHashSet();

        var now = DateTimeOffset.UtcNow;
        foreach (var (row, title, username, password, hasTotp) in logins)
        {
            if (string.IsNullOrEmpty(password))
            {
                findings.Add(new SecurityFinding(row.Id, title, FindingKind.EmptyPassword,
                    FindingSeverity.Medium, "Findings.EmptyPassword"));
                continue;
            }

            var strength = _estimator.Evaluate(password);
            if ((int)strength.Score <= 2)
            {
                var severity = reusedIds.Contains(row.Id) || (int)strength.Score == 0
                    ? FindingSeverity.Critical
                    : FindingSeverity.High;
                findings.Add(new SecurityFinding(row.Id, title, FindingKind.WeakPassword,
                    severity, "Findings.WeakPassword"));
            }

            if (reusedIds.Contains(row.Id))
            {
                findings.Add(new SecurityFinding(row.Id, title, FindingKind.ReusedPassword,
                    FindingSeverity.High, "Findings.ReusedPassword"));
            }

            var ageDays = (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(row.UpdatedAt)).TotalDays;
            if (ageDays > OldPasswordDays)
            {
                findings.Add(new SecurityFinding(row.Id, title, FindingKind.OldPassword,
                    FindingSeverity.Informational, "Findings.OldPassword"));
            }

            if (!hasTotp)
            {
                findings.Add(new SecurityFinding(row.Id, title, FindingKind.MissingAuthenticator,
                    FindingSeverity.Informational, "Findings.MissingAuthenticator"));
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        // Duplicate accounts: same normalized username + same normalized title.
        var byAccount = new Dictionary<string, List<SecurityFinding>>(StringComparer.Ordinal);
        foreach (var (row, title, username, _, _) in logins)
        {
            var normalizedUser = (username ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedTitle = title.Trim().ToLowerInvariant();
            if (normalizedUser.Length == 0)
            {
                continue;
            }

            var key = normalizedUser + "|" + normalizedTitle;
            if (!byAccount.TryGetValue(key, out var list))
            {
                byAccount[key] = list = [];
            }

            list.Add(new SecurityFinding(row.Id, title, FindingKind.DuplicateAccount,
                FindingSeverity.Medium, "Findings.DuplicateAccount"));
        }

        foreach (var list in byAccount.Values.Where(l => l.Count > 1))
        {
            findings.AddRange(list);
        }

        // Expiring secrets across kinds (API keys, licenses, cards).
        foreach (var row in repository.ListAllLive())
        {
            var kind = (ItemType)row.Type;
            if (kind is not (ItemType.ApiCredential or ItemType.SoftwareLicense or ItemType.PaymentCard))
            {
                continue;
            }

            var envelope = repository.GetCurrentPayload(row.Id);
            if (envelope is null)
            {
                continue;
            }

            var json = ItemEncryption.DecryptJson(session.Keyring, session.Header.VaultId, row.Id, envelope);
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(json);
                var root = document.RootElement;
                foreach (var key in new[] { "expiresAt" })
                {
                    if (!root.TryGetProperty("Data", out var dataElement) || dataElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!dataElement.TryGetProperty(key, out var expires) || expires.ValueKind != System.Text.Json.JsonValueKind.String)
                    {
                        continue;
                    }

                    if (DateOnly.TryParse(expires.GetString(), out var date))
                    {
                        var daysUntil = date.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
                        if (daysUntil <= ExpiringSoonDays)
                        {
                            var title = root.TryGetProperty("Title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                            findings.Add(new SecurityFinding(row.Id, title, FindingKind.ExpiringSecret,
                                daysUntil < 0 ? FindingSeverity.High : FindingSeverity.Medium,
                                daysUntil < 0 ? "Findings.Expired" : "Findings.ExpiringSoon"));
                        }
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }

        return new VaultSecurityReport(logins.Count, findings, DateTimeOffset.UtcNow);
    }

    

    private static string? GetString(System.Text.Json.JsonElement root, string property) =>
        root.TryGetProperty("Data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Object
            && data.TryGetProperty(property, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : root.TryGetProperty(property, out var legacy) && legacy.ValueKind == System.Text.Json.JsonValueKind.String
                ? legacy.GetString()
                : null;
}
