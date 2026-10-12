using System.Windows.Markup;

namespace Asgard.Hermod.UI.Localization;

/// <summary>XAML 文案统一使用共享资源；语言在启动阶段确定，避免运行中混用语种。</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    /// <summary>共享文案资源中的稳定键。</summary>
    public string Key { get; set; } = key;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) => Text.Get(Key);
}
