using System.Globalization;

namespace Asgard.Hermod.UI.Controls;

/// <summary>嵌入文档的滚动块只占用正文可用宽度，缩进不能把整个聊天窗口撑宽。</summary>
internal sealed class MarkdownViewportWidthConverter(double indent) : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width && double.IsFinite(width) ? Math.Max(1, width - indent) : 1d;
    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
