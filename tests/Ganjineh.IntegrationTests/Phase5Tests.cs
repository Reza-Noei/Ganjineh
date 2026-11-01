using System.Text;
using PasswordManager.Application.Items;
using PasswordManager.Application.Security;
using PasswordManager.Application.Vaults;
using PasswordManager.Cryptography.Otp;
using PasswordManager.Persistence.Items;
using PasswordManager.Persistence.Registry;
using Xunit;

namespace PasswordManager.IntegrationTests;

/// <summary>Phase 5: strength estimation, otpauth parsing, TOTP data keys, security analyzer.</summary>
public sealed class Phase5Tests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ganjineh-p5").FullName;

    private VaultLifecycleService Lifecycle() =>
        new(registryFactory: () => new VaultRegistry(Path.Combine(_root, "vaults.json")));

    private static VaultItemService Items() => new();
    private static VaultSecurityAnalyzer Analyzer() => new();

    // ── Strength estimator (zxcvbn-core wrapper) ──

    [Theory]
    [InlineData("password", StrengthScore.VeryWeak)]
    [InlineData("123456", StrengthScore.VeryWeak)]
    [InlineData("aaaaaaaaaa", StrengthScore.VeryWeak)]
    [InlineData("Tr0ub4dour&3", StrengthScore.Fair)]
    [InlineData("correcthorsebatterystaple", StrengthScore.VeryStrong)]
    public void Estimator_Matches_Known_Bands(string password, StrengthScore expected)
    {
        var estimator = new ZxcvbnStrengthEstimator();
        Assert.Equal(expected, estimator.Evaluate(password.ToCharArray()).Score);
    }

    [Fact]
    public void Estimator_Never_Crashes_On_Unicode_And_Empty()
    {
        var estimator = new ZxcvbnStrengthEstimator();
        Assert.True(estimator.Evaluate(string.Empty.ToCharArray()).Score == StrengthScore.VeryWeak);
        Assert.True(estimator.Evaluate("رمز-عبور-🔒".ToCharArray()).Score >= StrengthScore.Fair);
        Assert.True(estimator.Evaluate("کلمه".ToCharArray()).Score >= StrengthScore.VeryWeak);
    }

    // ── otpauth URI parsing ──

    [Fact]
    public void Otpauth_Parses_Full_Uri()
    {
        var secret = Base32.Encode(new byte[20]);
        var parsed = OtpauthUriParser.Parse(
            $"otpauth://totp/MyBank:ali@example.ir?secret={secret}&issuer=MyBank&algorithm=SHA256&digits=8&period=60");

        Assert.Equal("MyBank", parsed.Issuer);
        Assert.Equal("ali@example.ir", parsed.AccountName);
        Assert.Equal(secret, Base32.Encode(parsed.Secret));
        Assert.Equal(OtpAlgorithm.Sha256, parsed.Algorithm);
        Assert.Equal(8, parsed.Digits);
        Assert.Equal(60, parsed.PeriodSeconds);
    }

    [Theory]
    [InlineData("https://totp/x?secret=A")]
    [InlineData("otpauth://hotp/x?secret=A")]
    [InlineData("otpauth://totp/x")]
    [InlineData("otpauth://totp/x?secret=A&algorithm=MD5")]
    [InlineData("otpauth://totp/x?secret=A&digits=4")]
    [InlineData("otpauth://totp/x?secret=A&period=0")]
    [InlineData("otpauth://totp/x?secret=!!!!")]
    public void Otpauth_Rejects_Malformed_Input(string uri)
    {
        Assert.ThrowsAny<Exception>(() => OtpauthUriParser.Parse(uri));
    }

    // ── TOTP data keys ──

    [Fact]
    public void Totp_Config_RoundTrips_Through_Data_Keys()
    {
        var config = new TotpConfig(Base32.Decode("MFRGGZDFMZTWQ2LK"), "MyBank", "ali", OtpAlgorithm.Sha256, 8, 60);
        var data = new OtpApplicationService().ApplyTotpConfig(new Dictionary<string, object?>(), config);

        var restored = TotpConfig.FromData(data);
        Assert.NotNull(restored);
        Assert.Equal("MyBank", restored!.Issuer);
        Assert.Equal(OtpAlgorithm.Sha256, restored.Algorithm);
        Assert.Equal(8, restored.Digits);
        Assert.Equal(60, restored.PeriodSeconds);

        // Codes computed from restored config match codes computed from the original secret.
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        var engine = new OtpEngine();
        Assert.Equal(
            engine.Totp(new TotpConfiguration(config.Secret, config.Algorithm, config.Digits, config.PeriodSeconds), now).Code,
            engine.Totp(new TotpConfiguration(restored.Secret, restored.Algorithm, restored.Digits, restored.PeriodSeconds), now).Code);
    }

    [Fact]
    public async Task Totp_Secret_Is_Encrypted_At_Rest()
    {
        var lifecycle = Lifecycle();
        var items = Items();
        var otp = new OtpApplicationService();
        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "p5-رمز-1!".ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        var secret32 = Base32.Encode(new byte[20]);
        var data = otp.ApplyOtpauthUri(new Dictionary<string, object?>(),
            $"otpauth://totp/Ganjineh:me@x.ir?secret={secret32}&issuer=Ganjineh");
        var created = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "totp-item", Data = data,
        });
        session.Lock();

        var dbBytes = await File.ReadAllBytesAsync(Path.Combine(session.VaultDirectory, "vault.db"));
        Assert.Equal(-1, dbBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret32)));
        _ = created;
    }

    // ── Security analyzer ──

    [Fact]
    public async Task Analyzer_Detects_All_Finding_Classes_Without_Exposing_Passwords()
    {
        var lifecycle = Lifecycle();
        var items = Items();
        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "p5-رمز-1!".ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        var weak1 = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "weak-one", Data = new Dictionary<string, object?>
            {
                ["username"] = "a@x.ir", ["password"] = "password",
            },
        });
        var weak2 = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "weak-two", Data = new Dictionary<string, object?>
            {
                ["username"] = "a@x.ir", ["password"] = "password",
            },
        });
        _ = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login, Title = "empty-pass", Data = new Dictionary<string, object?>
            {
                ["username"] = "b@x.ir", ["password"] = null,
            },
        });
        var strong = await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.Login,
            Title = "strong-with-totp",
            Data = new Dictionary<string, object?>
            {
                ["username"] = "c@x.ir",
                ["password"] = "Xk9#mQ2$vL7@pR4!tW8&zB6^nH3*jF5",
                ["totpSecret"] = Base32.Encode(new byte[20]),
            },
        });

        var report = await Analyzer().AnalyzeAsync(session);

        Assert.Equal(4, report.TotalLogins);
        Assert.Contains(report.Findings, f => f.Kind == FindingKind.WeakPassword && f.Severity == FindingSeverity.Critical);
        Assert.Equal(2, report.Count(FindingKind.ReusedPassword));
        Assert.Equal(1, report.Count(FindingKind.EmptyPassword));
        // Empty-password items report only the empty-password finding (no finding stacking);
        // weak1 and weak2 lack an authenticator, strong-with-totp has one.
        Assert.Equal(2, report.Count(FindingKind.MissingAuthenticator));
        Assert.DoesNotContain(report.Findings, f => f.ItemId == strong.Id && f.Kind == FindingKind.MissingAuthenticator);
        Assert.DoesNotContain(report.Findings, f => f.ItemId == weak1.Id && f.Kind == FindingKind.EmptyPassword);

        // No plaintext secrets anywhere in the report.
        var reportText = string.Join('|', report.Findings.Select(f => f.ItemTitle + f.DetailKey));
        Assert.DoesNotContain("Xk9#mQ2", reportText, StringComparison.Ordinal);

        // Navigating to an affected item opens the real item (findings reference ids).
        Assert.Contains(report.Findings, f => f.ItemId == weak1.Id);
        Assert.Contains(report.Findings, f => f.ItemId == weak2.Id);
    }

    [Fact]
    public async Task Analyzer_Rejects_Locked_Session()
    {
        var lifecycle = Lifecycle();
        var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "p5-رمز-1!".ToCharArray() })
            .ContinueWith(t => t.Result.Session);
        session.Lock();

        await Assert.ThrowsAsync<StaleSessionException>(() => Analyzer().AnalyzeAsync(session));
    }

    [Fact]
    public async Task Expiring_Items_Are_Reported()
    {
        var lifecycle = Lifecycle();
        var items = Items();
        using var session = await lifecycle.CreateAsync(
            new CreateVaultRequest { Directory = _root, MasterPassword = "p5-رمز-1!".ToCharArray() })
            .ContinueWith(t => t.Result.Session);

        await items.CreateAsync(session, new ItemDraft
        {
            Kind = ItemType.ApiCredential,
            Title = "expiring-key",
            Data = new Dictionary<string, object?>
            {
                ["service"] = " payments ",
                ["expiresAt"] = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd"),
            },
        });

        var report = await Analyzer().AnalyzeAsync(session);
        Assert.Contains(report.Findings, f => f.Kind == FindingKind.ExpiringSecret);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
