namespace Asgard.Hermod.UI.Controls;

/// <summary>矢量绘制上圆角与两侧外展圆弧，选中底色直接接入正文，不绘制任何描边。</summary>
public sealed class MainNavigationTabChrome : FrameworkElement
{
    /// <summary>选中态由公共页签容器传入。</summary>
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(MainNavigationTabChrome), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>鼠标和键盘悬停使用同一套未选中提示。</summary>
    public static readonly DependencyProperty IsHighlightedProperty = DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(MainNavigationTabChrome), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>选中曲面与正文共用动态资源。</summary>
    public static readonly DependencyProperty BodyBrushProperty = DependencyProperty.Register(nameof(BodyBrush), typeof(Brush), typeof(MainNavigationTabChrome), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>未选中悬停底色。</summary>
    public static readonly DependencyProperty HoverBrushProperty = DependencyProperty.Register(nameof(HoverBrush), typeof(Brush), typeof(MainNavigationTabChrome), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>当前页签是否选中。</summary>
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    /// <summary>鼠标或键盘高亮。</summary>
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }
    /// <summary>正文同色背景。</summary>
    public Brush? BodyBrush { get => (Brush?)GetValue(BodyBrushProperty); set => SetValue(BodyBrushProperty, value); }
    /// <summary>未选中悬停背景。</summary>
    public Brush? HoverBrush { get => (Brush?)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }

    /// <summary>底部多覆盖一个物理像素，DPI 缩放和布局取整也不会产生标题栏露底。</summary>
    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (!IsSelected)
        {
            if (IsHighlighted) context.DrawRoundedRectangle(HoverBrush, null, new Rect(0, 0, width, Math.Max(0, height - 2)), 8, 8);
            return;
        }
        double bottom = height + 1 / VisualTreeHelper.GetDpi(this).DpiScaleY;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(-8, bottom), true, true);
            path.BezierTo(new Point(-3.58, bottom), new Point(0, height - 3.58), new Point(0, height - 8), true, false);
            path.LineTo(new Point(0, 8), true, false);
            path.BezierTo(new Point(0, 3.58), new Point(3.58, 0), new Point(8, 0), true, false);
            path.LineTo(new Point(width - 8, 0), true, false);
            path.BezierTo(new Point(width - 3.58, 0), new Point(width, 3.58), new Point(width, 8), true, false);
            path.LineTo(new Point(width, height - 8), true, false);
            path.BezierTo(new Point(width, height - 3.58), new Point(width + 3.58, bottom), new Point(width + 8, bottom), true, false);
        }
        geometry.Freeze(); context.DrawGeometry(BodyBrush, null, geometry);
    }
}
