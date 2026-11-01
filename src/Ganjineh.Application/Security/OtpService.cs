using System.Security.Cryptography;
using Ganjineh.Cryptography.Otp;

namespace Ganjineh.Application.Security;

/// <summary>Parsed otpauth:// URI (RFC-aligned subset).</summary>
public sealed record OtpauthUri(
    string Issuer,
    string AccountName,
    byte[] Secret,
    OtpAlgorithm Algorithm,
    int Digits,
    int PeriodSeconds);

/// <summary>
/// Parses `otpauth://totp/Issuer:Account?secret=…&issuer=…&algorithm=…&digits=…&period=…`.
/// Rejects unknown schemes, unsupported algorithms and out-of-range parameters (contract §6.4).
/// </summary>
public static class OtpauthUriParser
{
    public static OtpauthUri Parse(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Not an otpauth URI.");
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !parsed.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase)
            || !parsed.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Only otpauth://totp URIs are supported.");
        }

        var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);
        var secretText = query["secret"];
        if (string.IsNullOrWhiteSpace(secretText))
        {
            throw new FormatException("otpauth URI has no secret.");
        }

        byte[] secret;
        try
        {
            secret = Base32.Decode(secretText);
        }
        catch (FormatException)
        {
            throw new FormatException("otpauth secret is not valid Base32.");
        }

        if (secret.Length < 8)
        {
            throw new FormatException("otpauth secret too short (min 8 bytes).");
        }

        if (secretText.Length > 200 || uri.Length > 600)
        {
            throw new FormatException("otpauth URI is unreasonably long.");
        }

        var algorithm = query["algorithm"]?.ToUpperInvariant() switch
        {
            null or "" or "SHA1" => OtpAlgorithm.Sha1,
            "SHA256" => OtpAlgorithm.Sha256,
            "SHA512" => OtpAlgorithm.Sha512,
            _ => throw new FormatException("Unsupported otpauth algorithm."),
        };

        var digits = 6;
        if (query["digits"] is { } digitsText && (!int.TryParse(digitsText, out digits) || digits is < 6 or > 8))
        {
            throw new FormatException("otpauth digits must be 6–8.");
        }

        var period = 30;
        if (query["period"] is { } periodText && (!int.TryParse(periodText, out period) || period is < 1 or > 3600))
        {
            throw new FormatException("otpauth period must be 1–3600 seconds.");
        }

        // Label: /Issuer:Account or /Account
        var label = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
        string issuer = query["issuer"] ?? string.Empty, account = label;
        var separator = label.IndexOf(':');
        if (separator > 0)
        {
            issuer = issuer.Length > 0 ? issuer : label[..separator].Trim();
            account = label[(separator + 1)..].Trim();
        }

        return new OtpauthUri(issuer, account, secret, algorithm, digits, period);
    }
}

/// <summary>TOTP configuration bound to a Login item (stored encrypted in the payload Data keys).</summary>
public sealed record TotpConfig(
    byte[] Secret,
    string Issuer,
    string AccountName,
    OtpAlgorithm Algorithm,
    int Digits,
    int PeriodSeconds)
{
    /// <summary>Data-key projection (payload envelope storage — ItemPayload.Data).</summary>
    public Dictionary<string, object?> ToDataKeys() => new()
    {
        ["totpSecret"] = Base32.Encode(Secret),
        ["totpIssuer"] = Issuer,
        ["totpAccount"] = AccountName,
        ["totpAlgorithm"] = Algorithm.ToString().ToUpperInvariant(),
        ["totpDigits"] = Digits.ToString(),
        ["totpPeriod"] = PeriodSeconds.ToString(),
    };

    public static TotpConfig? FromData(IReadOnlyDictionary<string, object?> data)
    {
        if (data.GetValueOrDefault("totpSecret") is not string secretText || secretText.Length == 0)
        {
            return null;
        }

        var algorithm = data.GetValueOrDefault("totpAlgorithm")?.ToString()?.ToUpperInvariant() switch
        {
            "SHA256" => OtpAlgorithm.Sha256,
            "SHA512" => OtpAlgorithm.Sha512,
            _ => OtpAlgorithm.Sha1,
        };
        var digits = int.TryParse(data.GetValueOrDefault("totpDigits")?.ToString(), out var d) ? d : 6;
        var period = int.TryParse(data.GetValueOrDefault("totpPeriod")?.ToString(), out var p) ? p : 30;
        try
        {
            return new TotpConfig(
                Base32.Decode(secretText),
                data.GetValueOrDefault("totpIssuer")?.ToString() ?? string.Empty,
                data.GetValueOrDefault("totpAccount")?.ToString() ?? string.Empty,
                algorithm, digits, period);
        }
        catch (FormatException)
        {
            return null; // a corrupt stored secret surfaces as "no authenticator", never a crash
        }
    }
}

/// <summary>HOTP configuration for counter-based items (contract §7).</summary>
public sealed record HotpConfig(
    byte[] Secret,
    OtpAlgorithm Algorithm,
    int Digits,
    ulong Counter)
{
    public const string CounterKey = "hotpCounter";

    public static ulong ReadCounter(IReadOnlyDictionary<string, object?> data) =>
        ulong.TryParse(data.GetValueOrDefault(CounterKey)?.ToString(), out var counter) ? counter : 0;
}

/// <summary>
/// Application-level OTP facade: current codes for item payloads, otpauth import, and HOTP
/// counter advancement through the item service (transactional with the payload save).
/// </summary>
public interface IOtpApplicationService
{
    /// <summary>Generates the current TOTP code for a Data dictionary; null when no config.</summary>
    OtpResult? TotpNow(IReadOnlyDictionary<string, object?> data, DateTimeOffset nowUtc);

    /// <summary>Merges TOTP data keys into a draft dictionary (import path).</summary>
    Dictionary<string, object?> ApplyTotpConfig(Dictionary<string, object?> data, TotpConfig config);

    /// <summary>Imports an otpauth URI into TOTP data keys (throws FormatException on bad input).</summary>
    Dictionary<string, object?> ApplyOtpauthUri(Dictionary<string, object?> data, string uri);
}

public sealed class OtpApplicationService : IOtpApplicationService
{
    private readonly OtpEngine _engine = new();

    public OtpResult? TotpNow(IReadOnlyDictionary<string, object?> data, DateTimeOffset nowUtc)
    {
        var config = TotpConfig.FromData(data);
        if (config is null)
        {
            return null;
        }

        try
        {
            return _engine.Totp(new TotpConfiguration(config.Secret, config.Algorithm, config.Digits, config.PeriodSeconds), nowUtc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public Dictionary<string, object?> ApplyTotpConfig(Dictionary<string, object?> data, TotpConfig config)
    {
        foreach (var (key, value) in config.ToDataKeys())
        {
            data[key] = value;
        }

        return data;
    }

    public Dictionary<string, object?> ApplyOtpauthUri(Dictionary<string, object?> data, string uri)
    {
        var parsed = OtpauthUriParser.Parse(uri);
        return ApplyTotpConfig(data, new TotpConfig(
            parsed.Secret, parsed.Issuer, parsed.AccountName, parsed.Algorithm, parsed.Digits, parsed.PeriodSeconds));
    }
}

/// <summary>Random secret generation for the "create authenticator" flow (160-bit, Base32).</summary>
public static class OtpSecretFactory
{
    public static (string Base32Secret, byte[] Bytes) NewSecret() =>
        (Base32.Encode(RandomNumberGenerator.GetBytes(20)), RandomNumberGenerator.GetBytes(20));

    public static TotpConfig DefaultConfig(byte[] secret, string issuer, string account) =>
        new(secret, issuer, account, OtpAlgorithm.Sha1, 6, 30);
}
