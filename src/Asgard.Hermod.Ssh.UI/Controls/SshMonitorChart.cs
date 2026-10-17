using System.Globalization;
using System.Windows.Media;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Controls;

/// <summary>基于真实时间的轻量曲线；缺失点断线，固定百分比尺度与速率自动尺度分离。</summary>
public sealed class SshMonitorChart : Control
{
    /// <summary>主曲线数据。</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<SshMonitorPoint>), typeof(SshMonitorChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>可选第二曲线数据。</summary>
    public static readonly DependencyProperty SecondaryPointsProperty = DependencyProperty.Register(nameof(SecondaryPoints), typeof(IReadOnlyList<SshMonitorPoint>), typeof(SshMonitorChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>百分比固定一百；速率设置为零按两条曲线共同缩放。</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SshMonitorChart), new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>主曲线主题颜色。</summary>
    public static readonly DependencyProperty PrimaryBrushProperty = DependencyProperty.Register(nameof(PrimaryBrush), typeof(Brush), typeof(SshMonitorChart), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>次曲线主题颜色。</summary>
    public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.Register(nameof(SecondaryBrush), typeof(Brush), typeof(SshMonitorChart), new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>参考线主题颜色。</summary>
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(SshMonitorChart), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    private double? _hover;

    /// <summary>图表不进入键盘 Tab 序列，完整说明通过无障碍名称提供。</summary>
    public SshMonitorChart()
    {
        Focusable = false; IsTabStop = false; MinHeight = 100;
        SetResourceReference(ForegroundProperty, "TextMutedBrush");
        SetResourceReference(PrimaryBrushProperty, "AccentBrush");
        SetResourceReference(SecondaryBrushProperty, "SuccessBrush");
        SetResourceReference(GridBrushProperty, "LineSoftBrush");
        MouseMove += (_, e) => { _hover = e.GetPosition(this).X; UpdateTooltip(); InvalidateVisual(); };
        MouseLeave += (_, _) => { _hover = null; InvalidateVisual(); };
    }

    /// <summary>真实时间采样数组。</summary>
    public IReadOnlyList<SshMonitorPoint>? Points { get => (IReadOnlyList<SshMonitorPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    /// <summary>双线图的下行数据。</summary>
    public IReadOnlyList<SshMonitorPoint>? SecondaryPoints { get => (IReadOnlyList<SshMonitorPoint>?)GetValue(SecondaryPointsProperty); set => SetValue(SecondaryPointsProperty, value); }
    /// <summary>纵轴固定上限或零表示动态速率。</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>主曲线画笔。</summary>
    public Brush PrimaryBrush { get => (Brush)GetValue(PrimaryBrushProperty); set => SetValue(PrimaryBrushProperty, value); }
    /// <summary>次曲线画笔。</summary>
    public Brush SecondaryBrush { get => (Brush)GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }
    /// <summary>坐标参考线。</summary>
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    /// <summary>数字与时间刻度直接来自实际数据，不按点数平均铺开。</summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 140 || ActualHeight < 80) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var rect = PlotRect();
        double maximum = ScaleMaximum();
        var end = EndTime(); var start = end.AddMinutes(-2);
        for (int i = 0; i < 3; i++)
        {
            double y = rect.Top + rect.Height * i / 2;
            dc.DrawLine(new Pen(GridBrush, 1), new(rect.Left, y), new(rect.Right, y));
            DrawText(dc, Format(maximum * (2 - i) / 2), 0, y - 6, 9);
            var time = start.AddSeconds(i * 60).ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            var text = MakeText(time, 9);
            dc.DrawText(text, new Point(Math.Clamp(rect.Left + i * rect.Width / 2 - text.Width / 2, 0, Math.Max(0, ActualWidth - text.Width)), rect.Bottom + 8));
        }
        RenderSeries(dc, Points, PrimaryBrush, false, rect, start, maximum);
        RenderSeries(dc, SecondaryPoints, SecondaryBrush, true, rect, start, maximum);
        if (_hover is { } x && x >= rect.Left && x <= rect.Right)
            dc.DrawLine(new Pen(Foreground, 1) { DashStyle = DashStyles.Dot }, new(x, rect.Top), new(x, rect.Bottom));
        if (Points?.Any(p => p.Value is { } v && double.IsFinite(v)) != true && SecondaryPoints?.Any(p => p.Value is { } v && double.IsFinite(v)) != true)
        {
            var text = MakeText(Text.Get("SshMonitorWaitingChart"), 11);
            dc.DrawText(text, new Point(Math.Max(rect.Left, rect.Left + (rect.Width - text.Width) / 2), rect.Top + rect.Height / 2));
        }
    }

    private void RenderSeries(DrawingContext dc, IReadOnlyList<SshMonitorPoint>? values, Brush color, bool dashed, Rect rect, DateTimeOffset start, double maximum)
    {
        Point? previous = null; DateTimeOffset? previousTime = null;
        var pen = new Pen(color, 1.8) { DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var sample in values ?? [])
        {
            if (sample.Value is not { } value || !double.IsFinite(value) || value < 0) { previous = null; previousTime = null; continue; }
            double x = rect.Left + (sample.Timestamp - start).TotalSeconds / 120 * rect.Width;
            if (x < rect.Left || x > rect.Right + 1) { previous = null; previousTime = null; continue; }
            var point = new Point(Math.Min(x, rect.Right), rect.Bottom - Math.Clamp(value / maximum, 0, 1) * rect.Height);
            if (previous is { } before && previousTime is { } time && (sample.Timestamp - time).TotalSeconds <= 15) dc.DrawLine(pen, before, point);
            dc.DrawEllipse(color, null, point, 1.5, 1.5);
            previous = point; previousTime = sample.Timestamp;
        }
    }

    private Rect PlotRect() => new(64, 8, Math.Max(0, ActualWidth - 72), Math.Max(0, ActualHeight - 36));
    private DateTimeOffset EndTime() => (Points ?? []).Concat(SecondaryPoints ?? []).Select(p => p.Timestamp).DefaultIfEmpty(DateTimeOffset.UtcNow).Max();
    private double ScaleMaximum()
    {
        if (double.IsFinite(Maximum) && Maximum > 0) return Maximum;
        double value = (Points ?? []).Concat(SecondaryPoints ?? []).Where(p => p.Value is { } v && double.IsFinite(v) && v >= 0).Select(p => p.Value!.Value).DefaultIfEmpty(1024).Max();
        return Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(1024, value))));
    }
    private string Format(double? value) => value is { } v ? Maximum > 0 ? Text.Format("SshMonitorPercent", v) : SshMonitorViewModel.FormatRate(v) : Text.Get("SshMonitorNoData");
    private FormattedText MakeText(string text, double size) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    private void DrawText(DrawingContext dc, string text, double x, double y, double size) => dc.DrawText(MakeText(text, size), new Point(x, Math.Max(0, y)));

    private void UpdateTooltip()
    {
        if (_hover is not { } x || Points is not { Count: > 0 }) return;
        var rect = PlotRect(); if (rect.Width <= 0) return;
        var time = EndTime().AddSeconds(-120 + Math.Clamp((x - rect.Left) / rect.Width, 0, 1) * 120);
        var nearest = Points.MinBy(p => Math.Abs((p.Timestamp - time).TotalSeconds))!;
        var second = SecondaryPoints?.MinBy(p => Math.Abs((p.Timestamp - nearest.Timestamp).TotalSeconds));
        ToolTip = SecondaryPoints is not null
            ? Text.Format("SshMonitorTrafficTooltip", nearest.Timestamp.ToLocalTime(), Format(nearest.Value), Format(second?.Value))
            : Text.Format("SshMonitorPointTooltip", nearest.Timestamp.ToLocalTime(), Format(nearest.Value));
    }
}
