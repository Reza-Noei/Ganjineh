using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PasswordManager.Desktop.Localization;

/// <summary>Small converter set for template visibility switches.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>When true: non-null → Visible. When false: null → Visible.</summary>
    public bool WhenNotNull { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNotNull = value is not null;
        return isNotNull == WhenNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>0 / null → Visible (empty-state helper); Invert=true gives zero → Collapsed.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value is int count && count == 0;
        return isZero ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Empty/missing string → Visible (search watermark).</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// int value == int parameter (settings radio groups). ConvertBack yields the parameter when
/// checked so the radio writes its own index back to the bound property.
/// </summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int left && parameter is string right && int.TryParse(right, out var index) && left == index;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string text && int.TryParse(text, out var index)
            ? index
            : System.Windows.Data.Binding.DoNothing;
}

/// <summary>Resolves a localization resource KEY arriving through a binding.</summary>
public sealed class LocKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var localizer = Desktop.App.Services?.GetService(typeof(Application.Abstractions.ILocalizer))
            as Application.Abstractions.ILocalizer;
        return localizer is null || value is not string key ? value ?? string.Empty : localizer[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound string equals the string parameter (nav active states).
/// The write-back is a no-op: the section command owns the state, and ConvertBack throwing
/// would crash every nav radio click (ToggleButton writes IsChecked back through the binding).</summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}

/// <summary>ItemType → its Material icon (list rows, detail/editor headers).</summary>
public sealed class ItemTypeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Persistence.Items.ItemType kind ? Services.ItemIconMapper.For(kind) : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>ItemType → localized type label (detail headers, editor type picker).</summary>
public sealed class ItemTypeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Persistence.Items.ItemType kind)
        {
            return string.Empty;
        }

        var localizer = Desktop.App.Services?.GetService(typeof(Application.Abstractions.ILocalizer))
            as Application.Abstractions.ILocalizer;
        return localizer?[Services.ItemFormRegistry.LabelKeys[kind]] ?? kind.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>FindingKind → its Findings.* resource string (security center rows/chips).</summary>
public sealed class FindingKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Application.Security.FindingKind kind)
        {
            return string.Empty;
        }

        var localizer = Desktop.App.Services?.GetService(typeof(Application.Abstractions.ILocalizer))
            as Application.Abstractions.ILocalizer;
        return localizer?["Findings." + kind] ?? kind.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>FindingKind → localized detail string (DetailKey resource resolution).</summary>
public sealed class FindingDetailConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
        {
            return string.Empty;
        }

        var localizer = Desktop.App.Services?.GetService(typeof(Application.Abstractions.ILocalizer))
            as Application.Abstractions.ILocalizer;
        return localizer?[key] ?? key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
