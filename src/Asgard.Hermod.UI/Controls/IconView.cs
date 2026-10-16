namespace Asgard.Hermod.UI.Controls;

/// <summary>
/// 绘制内置品牌资源或 Lucide 几何；数据库品牌使用主题配色，通用图标继承前景色。
/// </summary>
public sealed class IconView : Control
{
    /// <summary>图标名称，对应内置品牌标识或 Pencil 的 Lucide 图标名。</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(IconView),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnKindChanged));

    /// <summary>独立动态资源会在换肤时触发重绘，不受调用方的通用图标颜色覆盖。</summary>
    private static readonly DependencyProperty DatabaseBrandBrushProperty = DependencyProperty.Register(
        "DatabaseBrandBrush", typeof(Brush), typeof(IconView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>数据库标识在所有入口统一选择主题品牌色，切换到通用图标时清除旧资源。</summary>
    private static void OnKindChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var icon = (IconView)target;
        string? key = args.NewValue switch
        {
            "brand-mysql" => "MySqlBrandBrush",
            "brand-postgresql" => "PostgreSqlBrandBrush",
            _ => null
        };
        if (key is not null) icon.SetResourceReference(DatabaseBrandBrushProperty, key);
        else icon.ClearValue(DatabaseBrandBrushProperty);
    }

    /// <summary>绑定主题资源键后使用动态资源，使文件类型颜色随换肤更新。</summary>
    public static readonly DependencyProperty ForegroundResourceKeyProperty = DependencyProperty.Register(
        nameof(ForegroundResourceKey), typeof(string), typeof(IconView),
        new PropertyMetadata(null, (target, args) =>
        {
            var icon = (IconView)target;
            if (args.NewValue is string { Length: > 0 } key) icon.SetResourceReference(ForegroundProperty, key);
            else icon.ClearValue(ForegroundProperty);
        }));

    /// <summary>文件类型提供的稳定主题色键；普通图标仍可直接设置 Foreground。</summary>
    public string? ForegroundResourceKey
    {
        get => (string?)GetValue(ForegroundResourceKeyProperty);
        set => SetValue(ForegroundResourceKeyProperty, value);
    }

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
        if (Kind == "brand-rabbitmq")
        {
            var bounds = RabbitMqBrand.Geometry.Bounds;
            double brandScale = Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height);
            if (brandScale <= 0) return;
            drawingContext.PushTransform(new MatrixTransform(brandScale, 0, 0, brandScale,
                (ActualWidth - bounds.Width * brandScale) / 2 - bounds.X * brandScale,
                (ActualHeight - bounds.Height * brandScale) / 2 - bounds.Y * brandScale));
            drawingContext.DrawGeometry(RabbitMqBrand.Brush, null, RabbitMqBrand.Geometry); drawingContext.Pop(); return;
        }
        if (ProviderIconCatalog.TryGet(Kind, out var brand))
        {
            double imageScale = Math.Min(ActualWidth / brand.Width, ActualHeight / brand.Height);
            if (imageScale <= 0) return;
            double width = brand.Width * imageScale;
            double height = brand.Height * imageScale;
            drawingContext.DrawImage(brand, new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height));
            return;
        }
        if (DatabaseBrandCatalog.TryGet(Kind, out var databaseBrand))
        {
            double brandScale = Math.Min(ActualWidth, ActualHeight) / 24d;
            drawingContext.PushTransform(new MatrixTransform(brandScale, 0, 0, brandScale,
                (ActualWidth - 24 * brandScale) / 2, (ActualHeight - 24 * brandScale) / 2));
            var brush = (Brush?)GetValue(DatabaseBrandBrushProperty) ?? Foreground;
            // 小尺寸填充路径的细线不足一个像素，轻微加粗避免抗锯齿后融入背景。
            var outline = brandScale <= 1 ? new Pen(brush, 0.5) { LineJoin = PenLineJoin.Round } : null;
            drawingContext.DrawGeometry(brush, outline, databaseBrand);
            drawingContext.Pop();
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
