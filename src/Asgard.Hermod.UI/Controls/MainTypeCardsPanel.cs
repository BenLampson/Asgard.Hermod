namespace Asgard.Hermod.UI.Controls;

/// <summary>四个等宽入口之间固定十四像素，首尾严格对齐正文边界。</summary>
public sealed class MainTypeCardsPanel : Panel
{
    private const double Gap = 14;
    /// <summary>从可用空间扣除三段间距再等分，不通过末项 Margin 改变卡片宽度。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1000;
        double cardWidth = Math.Max(0, (width - Gap * Math.Max(0, InternalChildren.Count - 1)) / Math.Max(1, InternalChildren.Count));
        foreach (UIElement child in InternalChildren) child.Measure(new Size(cardWidth, 94));
        return new Size(width, 94);
    }
    /// <summary>首卡从零开始，末卡右沿正好落在容器右沿。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = Math.Max(0, (finalSize.Width - Gap * Math.Max(0, InternalChildren.Count - 1)) / Math.Max(1, InternalChildren.Count));
        for (int i = 0; i < InternalChildren.Count; i++) InternalChildren[i].Arrange(new Rect(i * (width + Gap), 0, width, 94));
        return finalSize;
    }
}
