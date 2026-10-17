using System.Windows.Media;

namespace Asgard.Hermod.RabbitMq.UI.Controls;

/// <summary>绘制服务器最近 60 秒速率采样；没有采样时不生成示例曲线。</summary>
public sealed class RabbitMqRateChart : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(RabbitMqRecord), typeof(RabbitMqRateChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public RabbitMqRecord? Snapshot { get => (RabbitMqRecord?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var publish = Samples("message_stats.publish_details.samples"); if (publish.Count == 0) publish = Samples("message_stats.publish_in_details.samples");
        var delivery = Samples("message_stats.deliver_get_details.samples"); if (delivery.Count == 0) delivery = Samples("message_stats.publish_out_details.samples");
        var line = TryFindResource("RabbitMqLineBrush") as Brush ?? Brushes.Gray;
        double width = Math.Max(0, ActualWidth - 24), height = Math.Max(0, ActualHeight - 20);
        drawingContext.DrawLine(new Pen(line, 1), new(12, height), new(width + 12, height));
        double maximum = Math.Max(1, publish.Concat(delivery).Select(item => item.Value).DefaultIfEmpty(0).Max());
        long last = publish.Concat(delivery).Select(item => item.Time).DefaultIfEmpty(0).Max();
        Draw(publish, "RabbitMqAccentBrush", 0); Draw(delivery, "RabbitMqSuccessBrush", 1);
        if (publish.Count == 0 && delivery.Count == 0)
        {
            var text = new FormattedText(Text.Get("RabbitMqNoRateSamples"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12,
                TryFindResource("RabbitMqMutedBrush") as Brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(text, new(Math.Max(12, (ActualWidth - text.Width) / 2), height / 2));
        }
        void Draw(List<(long Time, double Value)> samples, string brush, int series)
        {
            double slot = width / 12, bar = Math.Max(1, (slot - 21) / 2);
            foreach (var sample in samples.Where(item => item.Time > last - 60000).OrderBy(item => item.Time))
            {
                int index = Math.Clamp(11 - (int)((last - sample.Time) / 5000), 0, 11);
                double barHeight = sample.Value / maximum * height;
                drawingContext.DrawRoundedRectangle(TryFindResource(brush) as Brush ?? Brushes.SteelBlue, null,
                    new Rect(12 + index * slot + series * (bar + 3), height - barHeight, bar, barHeight), 2, 2);
            }
        }
    }
    private List<(long Time, double Value)> Samples(string path)
    {
        var data = Snapshot?.Value(path) ?? default; var result = new List<(long, double)>();
        if (data.ValueKind != JsonValueKind.Array) return result;
        // management 的 rate samples 是累计计数，对时间相邻点差分才是每秒速率。
        var points = data.EnumerateArray().Select(item => new RabbitMqRecord(item)).Where(item => item.Number("timestamp") is not null && item.Number("sample") is not null).OrderBy(item => item.Number("timestamp")).ToArray();
        for (int index = 1; index < points.Length; index++)
        {
            double elapsed = (points[index].Number("timestamp")!.Value - points[index - 1].Number("timestamp")!.Value) / 1000d;
            double delta = points[index].Number("sample")!.Value - points[index - 1].Number("sample")!.Value;
            if (elapsed > 0 && delta >= 0) result.Add(((long)points[index].Number("timestamp")!.Value, delta / elapsed));
        }
        return result;
    }
}
