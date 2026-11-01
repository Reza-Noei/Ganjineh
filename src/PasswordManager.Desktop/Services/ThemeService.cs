using System.Windows;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;



namespace PasswordManager.Desktop.Services;

/// <summary>
/// Live theme switching, Material Design edition. Two swaps per Apply():
/// 1. the <see cref="BundledTheme"/> ResourceDictionary (regenerates every Material brush +
///    color scheme for Light/Dark), and
/// 2. the Ganjineh brand-token dictionary (Themes/Light|Dark.xaml), whose brushes are consumed
///    via DynamicResource, so both swaps propagate without restart.
/// </summary>
public sealed class ThemeService
{
    private static readonly Uri LightUri =
        new("pack://application:,,,/PasswordManager.Desktop;component/Themes/Light.xaml");

    private static readonly Uri DarkUri =
        new("pack://application:,,,/PasswordManager.Desktop;component/Themes/Dark.xaml");

    private const PrimaryColor Primary = PrimaryColor.Indigo;
    private const SecondaryColor Secondary = SecondaryColor.Indigo;

    /// <summary>Normalized current theme: "Light" or "Dark".</summary>
    public string Current { get; private set; } = "Light";

    public void Apply(string theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var isDark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;

        // 1) Material Design: replace the BundledTheme instance so all MD brushes re-resolve.
        var bundled = new BundledTheme
        {
            BaseTheme = isDark ? BaseTheme.Dark : BaseTheme.Light,
            PrimaryColor = Primary,
            SecondaryColor = Secondary,
        };
        var bundledIndex = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i] is BundledTheme)
            {
                bundledIndex = i;
                break;
            }
        }
        if (bundledIndex >= 0)
        {
            merged[bundledIndex] = bundled;
        }
        else
        {
            merged.Insert(0, bundled);
        }

        // 2) Brand tokens: swap the Themes/* dictionary (matched by URI, not "/Themes/" — the MD
        //    defaults dictionary also lives under a Themes path).
        var tokenDictionary = new ResourceDictionary { Source = isDark ? DarkUri : LightUri };
        var existing = merged.FirstOrDefault(d =>
            Uri.Equals(d.Source, LightUri) || Uri.Equals(d.Source, DarkUri));
        if (existing is not null)
        {
            merged[merged.IndexOf(existing)] = tokenDictionary;
        }
        else
        {
            merged.Insert(1, tokenDictionary);
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
