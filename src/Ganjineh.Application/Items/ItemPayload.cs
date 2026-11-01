using System.Globalization;
using System.Text;
using System.Text.Json;
using Ganjineh.Application.Credentials;
using Ganjineh.Persistence.Items;

namespace Ganjineh.Application.Items;

/// <summary>Custom field types (custom-fields.md). Values are strings; semantics per type.</summary>
public enum CustomFieldType : int
{
    Text = 1,
    Concealed = 2,
    Multiline = 3,
    Url = 4,
    Email = 5,
    Number = 6,
    Date = 7,
    Boolean = 8,
}

public sealed record CustomField(
    string Id,
    string Label,
    CustomFieldType Type,
    string? Value,
    int Order,
    bool Concealed);

/// <summary>
/// Canonical item payload, version 1 (vault-item-model.md). One envelope covers the title,
/// description, custom fields and the type-specific data object — all secret-class content is
/// inside this single authenticated envelope.
/// </summary>
public sealed class ItemPayload
{
    public const int SchemaVersion = 1;

    public int V { get; init; } = SchemaVersion;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<CustomField> Fields { get; set; } = [];
    public Dictionary<string, object?>? Data { get; set; }

    public string Serialize() => System.Text.Json.JsonSerializer.Serialize(this);

    /// <summary>
    /// Parses a payload envelope plaintext. Phase 3 logins stored the legacy {"T","U","P","W","N"}
    /// shape; those are upgraded in memory to v1 Login payloads (on-disk upgrade happens lazily on
    /// the next save — no destructive migration of existing ciphertext).
    /// </summary>
    public static ItemPayload Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("V", out _))
        {
            return System.Text.Json.JsonSerializer.Deserialize<ItemPayload>(json)
                ?? throw new InvalidOperationException("Empty payload.");
        }

        return ParseLegacy(root);
    }

    public static ItemPayload Parse(byte[] json) => Parse(Encoding.UTF8.GetString(json));

    private static ItemPayload ParseLegacy(JsonElement root)
    {
        // Legacy Phase 3 login payload.
        string? T = null, U = null, P = null, W = null, N = null;
        if (root.TryGetProperty("T", out var t)) T = t.GetString();
        if (root.TryGetProperty("U", out var u)) U = u.GetString();
        if (root.TryGetProperty("P", out var p)) P = p.GetString();
        if (root.TryGetProperty("W", out var w)) W = w.GetString();
        if (root.TryGetProperty("N", out var n)) N = n.GetString();

        return new ItemPayload
        {
            Title = T ?? string.Empty,
            Description = N,
            Data = new Dictionary<string, object?>
            {
                ["urls"] = W is null ? new List<string>() : new List<string> { W },
                ["username"] = U,
                ["password"] = P,
            },
        };
    }
}

/// <summary>Type-aware validation for custom field values (values are stored as strings).</summary>
public static class FieldValidation
{
    public static void Validate(CustomField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (string.IsNullOrWhiteSpace(field.Label))
        {
            throw new ValidationException("FieldLabelRequired");
        }

        if (field.Label.Length > 100)
        {
            throw new ValidationException("FieldLabelTooLong");
        }

        if (field.Type is CustomFieldType.Url or CustomFieldType.Email or CustomFieldType.Number
            or CustomFieldType.Date or CustomFieldType.Boolean
            && string.IsNullOrEmpty(field.Value))
        {
            return; // optional unless set
        }

        switch (field.Type)
        {
            case CustomFieldType.Url when !Uri.TryCreate(field.Value, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"):
                throw new ValidationException("FieldInvalidUrl");
            case CustomFieldType.Email when field.Value is null || !field.Value.Contains('@'):
                throw new ValidationException("FieldInvalidEmail");
            case CustomFieldType.Number when !double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                throw new ValidationException("FieldInvalidNumber");
            case CustomFieldType.Date when !DateOnly.TryParse(field.Value, CultureInfo.InvariantCulture, out _):
                throw new ValidationException("FieldInvalidDate");
            case CustomFieldType.Boolean when field.Value is not ("true" or "false"):
                throw new ValidationException("FieldInvalidBoolean");
        }
    }
}

public static class ItemKindInfo
{
    /// <summary>Data keys whose values may enter the search index. Concealed/secret keys excluded.</summary>
    public static readonly IReadOnlyDictionary<ItemType, string[]> SearchableDataKeys =
        new Dictionary<ItemType, string[]>
        {
            [ItemType.Login] = ["username", "urls"],
            [ItemType.SecureNote] = ["category"],
            [ItemType.PaymentCard] = ["holder", "issuer"],
            [ItemType.Identity] = ["fullName", "emails", "phones", "address", "idNumbers"],
            [ItemType.Wifi] = ["ssid", "security"],
            [ItemType.ApiCredential] = ["service", "endpoint", "environment"],
            [ItemType.SoftwareLicense] = ["product", "email", "vendorUrl"],
            [ItemType.RecoveryCodes] = ["service"],
            [ItemType.Custom] = [],
        };

    /// <summary>Short subtitle source key per kind (display only).</summary>
    public static readonly IReadOnlyDictionary<ItemType, string> SubtitleKey =
        new Dictionary<ItemType, string>
        {
            [ItemType.Login] = "username",
            [ItemType.SecureNote] = "category",
            [ItemType.PaymentCard] = "holder",
            [ItemType.Identity] = "fullName",
            [ItemType.Wifi] = "ssid",
            [ItemType.ApiCredential] = "service",
            [ItemType.SoftwareLicense] = "product",
            [ItemType.RecoveryCodes] = "service",
            [ItemType.Custom] = "",
        };

    public static string? SubtitleFor(ItemType kind, Dictionary<string, object?>? data)
    {
        if (data is null || !SubtitleKey.TryGetValue(kind, out var key) || key.Length == 0)
        {
            return null;
        }

        return data.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
