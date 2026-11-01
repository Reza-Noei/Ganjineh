using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Ganjineh.Application.Abstractions;

namespace Ganjineh.Desktop.Localization;

/// <summary>
/// XAML markup extension: Text="{loc:Loc Nav.AllItems}". Binds to the ILocalizer string indexer
/// so live language switches refresh every bound string. Falls back to the raw key at design time.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension()
    {
        Key = string.Empty;
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (App.Services?.GetService(typeof(ILocalizer)) is not ILocalizer localizer || Key.Length == 0)
        {
            return Key;
        }

        return new Binding
        {
            Source = localizer,
            Path = new PropertyPath("Item[" + Key + "]"),
            Mode = BindingMode.OneWay,
        }.ProvideValue(serviceProvider);
    }
}
