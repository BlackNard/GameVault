using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace GameVault.App;

/// <summary>
/// <c>{loc:Str Settings.Language}</c>: the text for a resource key, updated live when the language changes
/// (declare <c>xmlns:loc="clr-namespace:GameVault.App"</c>). Where the target is not a dependency property it is a plain lookup.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension() { }
    public StrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget { TargetProperty: DependencyProperty })
            return new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
        return Loc.T(Key);
    }
}
