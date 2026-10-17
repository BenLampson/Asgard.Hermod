namespace Asgard.Hermod.UI.Localization;

/// <summary>固定环境的显示翻译不会写入标签实体或影响业务排序。</summary>
public sealed class BuiltInTagNameConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is ManagedTag tag ? tag.IsBuiltIn ? TagDisplayText.Name("Environment/" + tag.Name, tag.Name) : tag.Name : "";

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Binding.DoNothing;
}
