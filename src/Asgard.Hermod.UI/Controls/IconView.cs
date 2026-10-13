namespace Asgard.Hermod.UI.Controls;

/// <summary>
/// 绘制内置官方品牌资源或 Lucide 几何；只有通用图标继承主题前景色。
/// </summary>
public sealed class IconView : Control
{
    /// <summary>图标名称，对应内置品牌标识或 Pencil 的 Lucide 图标名。</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(IconView),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>设置默认尺寸和装饰性行为，使图标不抢占键盘焦点。</summary>
    static IconView()
    {
        WidthProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(16d));
        HeightProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(16d));
        FocusableProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
        ForegroundProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(
            Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    /// <summary>选择图标；未知名称显示为空白，避免外部数据导致界面异常。</summary>
    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>按 24 × 24 原始画布等比缩放，保留 Lucide 圆角描边。</summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ProviderIconCatalog.TryGet(Kind, out var brand))
        {
            double imageScale = Math.Min(ActualWidth / brand.Width, ActualHeight / brand.Height);
            if (imageScale <= 0) return;
            double width = brand.Width * imageScale;
            double height = brand.Height * imageScale;
            drawingContext.DrawImage(brand, new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height));
            return;
        }
        if (!IconCatalog.TryGet(Kind, out var geometry) || Foreground is null)
        {
            return;
        }

        var scale = Math.Min(ActualWidth, ActualHeight) / 24d;
        if (scale <= 0d)
        {
            return;
        }

        var offsetX = (ActualWidth - 24d * scale) / 2d;
        var offsetY = (ActualHeight - 24d * scale) / 2d;
        var matrix = new Matrix(scale, 0d, 0d, scale, offsetX, offsetY);
        var pen = new Pen(Foreground, 2d)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        drawingContext.PushTransform(new MatrixTransform(matrix));
        drawingContext.DrawGeometry(null, pen, geometry);
        drawingContext.Pop();
    }
}
