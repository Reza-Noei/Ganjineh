using System.Windows;




namespace PasswordManager.Desktop.Services;

/// <summary>
/// Swaps the theme ResourceDictionary in Application.Resources.MergedDictionaries; all styles
/// reference tokens via DynamicResource, so the swap propagates live without restart.
/// </summary>
public sealed class ThemeService
{
    private static readonly Uri LightUri =
        new("pack://application:,,,/PasswordManager.Desktop;component/Themes/Light.xaml");

    private static readonly Uri DarkUri =
        new("pack://application:,,,/PasswordManager.Desktop;component/Themes/Dark.xaml");

    /// <summary>Normalized current theme: "Light" or "Dark".</summary>
    public string Current { get; private set; } = "Light";

    public void Apply(string theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var isDark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        var dictionary = new ResourceDictionary { Source = isDark ? DarkUri : LightUri };

        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("/Themes/", StringComparison.Ordinal) == true);
        if (existing is not null)
        {
            merged[merged.IndexOf(existing)] = dictionary;
        }
        else
        {
            merged.Insert(0, dictionary);
        }

        Current = isDark ? "Dark" : "Light";
    }

    /// <summary>Switches to the opposite theme and returns its normalized name.</summary>
    public string Toggle()
    {
        var next = Current == "Dark" ? "Light" : "Dark";
        Apply(next);
        return next;
    }
}
