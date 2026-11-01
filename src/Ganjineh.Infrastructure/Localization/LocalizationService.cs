using System.ComponentModel;
using System.Globalization;
using System.Resources;
using PasswordManager.Application.Abstractions;

namespace PasswordManager.Infrastructure.Localization;

/// <summary>
/// resx-backed localizer. The neutral (invariant) resource file holds Persian; Strings.en.resx is
/// the English satellite. Live switching raises both CultureChanged (shell FlowDirection) and
/// Item[] (all indexer bindings refresh).
/// </summary>
public sealed class LocalizationService : ILocalizer
{
    private static readonly ResourceManager Strings =
        new("PasswordManager.Infrastructure.Localization.Strings", typeof(LocalizationService).Assembly);

    private CultureInfo _culture = new("fa");

    public string this[string key] => Strings.GetString(key, _culture) ?? $"[{key}]";

    public CultureInfo CurrentCulture => _culture;

    public bool IsRightToLeft => _culture.TextInfo.IsRightToLeft;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CultureChanged;

    public void SetCulture(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var culture = CultureInfo.GetCultureInfo(name);
        if (culture.Equals(_culture))
        {
            return;
        }

        _culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightToLeft)));
    }
}
