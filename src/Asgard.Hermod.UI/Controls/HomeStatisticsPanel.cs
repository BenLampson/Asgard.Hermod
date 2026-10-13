namespace Asgard.Hermod.UI.Controls;

/// <summary>首页统计等宽分栏，窄窗口按最小卡片宽度换行，不产生横向滚动。</summary>
public sealed class HomeStatisticsPanel : Panel
{
    private const double Gap = 12;
    private const double MinimumCardWidth = 170;
    private int _columns = 1;
    private double _rowHeight;

    /// <summary>依照可用宽度计算分栏，卡片内容决定每行高度。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinimumCardWidth;
        _columns = Math.Max(1, Math.Min(InternalChildren.Count,
            (int)Math.Floor((width + Gap) / (MinimumCardWidth + Gap))));
        double cardWidth = Math.Max(0, (width - Gap * (_columns - 1)) / _columns);
        _rowHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cardWidth, double.PositiveInfinity));
            _rowHeight = Math.Max(_rowHeight, child.DesiredSize.Height);
        }
        int rows = (InternalChildren.Count + _columns - 1) / _columns;
        return new Size(width, rows == 0 ? 0 : rows * _rowHeight + (rows - 1) * Gap);
    }

    /// <summary>与测量使用相同的间距，末行保留同等卡片宽度。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double cardWidth = Math.Max(0, (finalSize.Width - Gap * (_columns - 1)) / _columns);
        for (int index = 0; index < InternalChildren.Count; index++)
            InternalChildren[index].Arrange(new Rect(index % _columns * (cardWidth + Gap),
                index / _columns * (_rowHeight + Gap), cardWidth, _rowHeight));
        return finalSize;
    }
}
