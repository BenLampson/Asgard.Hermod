namespace Asgard.Hermod.UI.Controls;

/// <summary>最近访问最多三列六张卡片，窄窗口按最小宽度降到两列或一列。</summary>
public sealed class HomeRecentPanel : Panel
{
    private const double Gap = 12;
    private const double MinimumCardWidth = 240;
    private int _columns = 1;
    private double _rowHeight;
    /// <summary>按实际可用宽度测量，少于六项时保持相同的列宽而不填充假记录。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinimumCardWidth;
        _columns = Math.Clamp((int)Math.Floor((width + Gap) / (MinimumCardWidth + Gap)), 1, 3);
        double cardWidth = Math.Max(0, (width - Gap * (_columns - 1)) / _columns); _rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        { child.Measure(new Size(cardWidth, double.PositiveInfinity)); _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height); }
        int rows = (InternalChildren.Count + _columns - 1) / _columns;
        return new(width, rows == 0 ? 0 : rows * _rowHeight + (rows - 1) * Gap);
    }
    /// <summary>卡片按访问时间从左到右、从上到下排列，卡片之间保留固定间距。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double cardWidth = Math.Max(0, (finalSize.Width - Gap * (_columns - 1)) / _columns);
        for (int index = 0; index < InternalChildren.Count; index++)
            InternalChildren[index].Arrange(new Rect(index % _columns * (cardWidth + Gap), index / _columns * (_rowHeight + Gap), cardWidth, _rowHeight));
        return finalSize;
    }
}
