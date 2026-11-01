using MaterialDesignThemes.Wpf;
using PasswordManager.Persistence.Items;

namespace PasswordManager.Desktop.Services;

/// <summary>
/// Visual identity per item kind: the same Material icon is used in the nav categories, the item
/// list rows and the details/editor headers so each type reads consistently across the shell.
/// </summary>
public static class ItemIconMapper
{
    public static readonly IReadOnlyDictionary<ItemType, PackIconKind> Icons =
        new Dictionary<ItemType, PackIconKind>
        {
            [ItemType.Login] = PackIconKind.Key,
            [ItemType.SecureNote] = PackIconKind.NoteText,
            [ItemType.PaymentCard] = PackIconKind.CreditCard,
            [ItemType.Identity] = PackIconKind.CardAccountDetails,
            [ItemType.Wifi] = PackIconKind.Wifi,
            [ItemType.ApiCredential] = PackIconKind.Api,
            [ItemType.SoftwareLicense] = PackIconKind.Certificate,
            [ItemType.RecoveryCodes] = PackIconKind.Dialpad,
            [ItemType.Custom] = PackIconKind.PackageVariantClosed,
        };

    public static PackIconKind For(ItemType kind) =>
        Icons.GetValueOrDefault(kind, PackIconKind.PackageVariantClosed);
}
