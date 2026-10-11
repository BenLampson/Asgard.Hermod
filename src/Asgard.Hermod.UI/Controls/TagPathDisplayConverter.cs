namespace Asgard.Hermod.UI.Controls;

/// <summary>选择摘要只展示标签路径最后两层，实际筛选仍使用原始完整路径。</summary>
public sealed class TagPathDisplayConverter : IValueConverter
{
    /// <summary>将相邻标签层级以带空格的斜杠连接，单层路径保留原名称。</summary>
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is string path ? string.Join(" / ", path.Split('/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(2)) : "";

    /// <summary>摘要为只读显示，转换不会写回或改变筛选路径。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Binding.DoNothing;
}
