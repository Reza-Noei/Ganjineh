using PasswordManager.Persistence.Items;

namespace PasswordManager.Desktop.Services;

public enum InputKind
{
    Text,
    Password,
    Multiline,
    Number,
    Date,
    Boolean,
    Email,
    Url,
    TextList,
}

/// <summary>One editable row of a type-specific form (metadata-driven editor + details).</summary>
public sealed record FieldSpec(string DataKey, string LabelKey, InputKind Input, bool Searchable);

/// <summary>
/// Per-item-kind form metadata. The editor and details pane render from these specs, so adding a
/// type never means hand-building another form.
/// </summary>
public static class ItemFormRegistry
{
    public static readonly IReadOnlyDictionary<ItemType, string> Glyphs =
        new Dictionary<ItemType, string>
        {
            [ItemType.Login] = "🔑",
            [ItemType.SecureNote] = "📝",
            [ItemType.PaymentCard] = "💳",
            [ItemType.Identity] = "🪪",
            [ItemType.Wifi] = "📶",
            [ItemType.ApiCredential] = "⚙",
            [ItemType.SoftwareLicense] = "🧾",
            [ItemType.RecoveryCodes] = "🛟",
            [ItemType.Custom] = "📦",
        };

    public static readonly IReadOnlyDictionary<ItemType, string> LabelKeys =
        new Dictionary<ItemType, string>
        {
            [ItemType.Login] = "Type.Login",
            [ItemType.SecureNote] = "Type.Note",
            [ItemType.PaymentCard] = "Type.Card",
            [ItemType.Identity] = "Type.Identity",
            [ItemType.Wifi] = "Type.Wifi",
            [ItemType.ApiCredential] = "Type.Api",
            [ItemType.SoftwareLicense] = "Type.License",
            [ItemType.RecoveryCodes] = "Type.Codes",
            [ItemType.Custom] = "Type.Custom",
        };

    public static readonly IReadOnlyDictionary<ItemType, FieldSpec[]> Fields =
        new Dictionary<ItemType, FieldSpec[]>
        {
            [ItemType.Login] =
            [
                new("username", "Details.Username", InputKind.Text, Searchable: true),
                new("password", "Details.Password", InputKind.Password, Searchable: false),
                new("urls", "Details.Website", InputKind.TextList, Searchable: true),
            ],
            [ItemType.SecureNote] =
            [
                new("content", "Details.Content", InputKind.Multiline, Searchable: false),
                new("category", "Details.Category", InputKind.Text, Searchable: true),
            ],
            [ItemType.PaymentCard] =
            [
                new("holder", "Card.Holder", InputKind.Text, Searchable: true),
                new("number", "Card.Number", InputKind.Password, Searchable: false),
                new("expMonth", "Card.ExpMonth", InputKind.Number, Searchable: false),
                new("expYear", "Card.ExpYear", InputKind.Number, Searchable: false),
                new("cvv", "Card.Cvv", InputKind.Password, Searchable: false),
                new("issuer", "Card.Issuer", InputKind.Text, Searchable: true),
            ],
            [ItemType.Identity] =
            [
                new("fullName", "Identity.FullName", InputKind.Text, Searchable: true),
                new("dob", "Identity.Dob", InputKind.Date, Searchable: false),
                new("emails", "Identity.Emails", InputKind.TextList, Searchable: true),
                new("phones", "Identity.Phones", InputKind.TextList, Searchable: true),
                new("address", "Identity.Address", InputKind.Multiline, Searchable: true),
                new("idNumbers", "Identity.IdNumbers", InputKind.Password, Searchable: false),
            ],
            [ItemType.Wifi] =
            [
                new("ssid", "Wifi.Ssid", InputKind.Text, Searchable: true),
                new("security", "Wifi.Security", InputKind.Text, Searchable: true),
                new("password", "Details.Password", InputKind.Password, Searchable: false),
                new("hidden", "Wifi.Hidden", InputKind.Boolean, Searchable: false),
            ],
            [ItemType.ApiCredential] =
            [
                new("service", "Api.Service", InputKind.Text, Searchable: true),
                new("apiKey", "Api.Key", InputKind.Password, Searchable: false),
                new("apiSecret", "Api.Secret", InputKind.Password, Searchable: false),
                new("token", "Api.Token", InputKind.Password, Searchable: false),
                new("endpoint", "Api.Endpoint", InputKind.Url, Searchable: true),
                new("environment", "Api.Environment", InputKind.Text, Searchable: true),
                new("expiresAt", "Api.Expires", InputKind.Date, Searchable: false),
            ],
            [ItemType.SoftwareLicense] =
            [
                new("product", "License.Product", InputKind.Text, Searchable: true),
                new("key", "License.Key", InputKind.Password, Searchable: false),
                new("email", "License.Email", InputKind.Email, Searchable: true),
                new("purchasedAt", "License.Purchased", InputKind.Date, Searchable: false),
                new("expiresAt", "License.Expires", InputKind.Date, Searchable: false),
                new("vendorUrl", "License.Vendor", InputKind.Url, Searchable: true),
            ],
            [ItemType.RecoveryCodes] =
            [
                new("service", "Codes.Service", InputKind.Text, Searchable: true),
                new("codes", "Codes.Codes", InputKind.Password, Searchable: false),
            ],
            [ItemType.Custom] = [],
        };
}
