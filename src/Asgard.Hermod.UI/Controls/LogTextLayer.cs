using System.Globalization;

namespace Asgard.Hermod.UI.Controls;

/// <summary>只绘制日志可见行的语义颜色；选区、查找、复制和原文仍由原生 TextBox 拥有。</summary>
public sealed class LogTextLayer : FrameworkElement
{
    /// <summary>普通日志颜色，主题变化时重绘。</summary>
    public static readonly DependencyProperty BodyBrushProperty = DependencyProperty.Register(nameof(BodyBrush), typeof(Brush), typeof(LogTextLayer), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>警告整行颜色。</summary>
    public static readonly DependencyProperty WarningBrushProperty = DependencyProperty.Register(nameof(WarningBrush), typeof(Brush), typeof(LogTextLayer), new FrameworkPropertyMetadata(Brushes.DarkOrange, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>错误整行颜色。</summary>
    public static readonly DependencyProperty ErrorBrushProperty = DependencyProperty.Register(nameof(ErrorBrush), typeof(Brush), typeof(LogTextLayer), new FrameworkPropertyMetadata(Brushes.DarkRed, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>普通日志画刷。</summary>
    public Brush BodyBrush { get => (Brush)GetValue(BodyBrushProperty); set => SetValue(BodyBrushProperty, value); }
    /// <summary>警告画刷。</summary>
    public Brush WarningBrush { get => (Brush)GetValue(WarningBrushProperty); set => SetValue(WarningBrushProperty, value); }
    /// <summary>错误画刷。</summary>
    public Brush ErrorBrush { get => (Brush)GetValue(ErrorBrushProperty); set => SetValue(ErrorBrushProperty, value); }
    /// <summary>快照行号颜色。</summary>
    public static readonly DependencyProperty GutterBrushProperty = DependencyProperty.Register(nameof(GutterBrush), typeof(Brush), typeof(LogTextLayer), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>行号画刷。</summary>
    public Brush GutterBrush { get => (Brush)GetValue(GutterBrushProperty); set => SetValue(GutterBrushProperty, value); }
    /// <summary>选区与搜索命中底色；输入焦点移到搜索框后仍保留阅读位置。</summary>
    public static readonly DependencyProperty SelectionBrushProperty = DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(LogTextLayer), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>当前主题的日志选区颜色。</summary>
    public Brush SelectionBrush { get => (Brush)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    private TextBox? _input;
    private TextBlock? _numbers;
    private string? _text;
    private int[] _starts = [0];
    private object? _layout;

    /// <summary>图层没有交互或远端行为，颜色使用动态资源。</summary>
    public LogTextLayer()
    {
        IsHitTestVisible = false; ClipToBounds = true;
        SetResourceReference(BodyBrushProperty, "LogInkBrush"); SetResourceReference(WarningBrushProperty, "LogWarningBrush"); SetResourceReference(ErrorBrushProperty, "LogErrorBrush"); SetResourceReference(GutterBrushProperty, "LogGutterBrush");
        SetResourceReference(SelectionBrushProperty, "LogSelectionBrush");
    }
    /// <summary>只连接同一编辑器拥有的输入控件，不订阅全局事件。</summary>
    internal void Attach(TextBox input, TextBlock numbers) { _input = input; _numbers = numbers; input.LayoutUpdated += OnLayoutUpdated; input.SelectionChanged += (_, _) => InvalidateVisual(); }
    /// <summary>跨显示器缩放变化后重新按目标 DPI 绘制字形。</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); _layout = null; InvalidateVisual(); }
    private void OnLayoutUpdated(object? sender, EventArgs args)
    {
        if (_input is not { IsArrangeValid: true } input || Visibility != Visibility.Visible) return;
        object current = (input.Text, input.FontSize, TextBlock.GetLineHeight(input), input.TextWrapping, input.HorizontalOffset, input.VerticalOffset, input.ViewportWidth, input.ViewportHeight, input.LineCount);
        if (Equals(_layout, current)) return;
        _layout = current; InvalidateVisual();
    }

    /// <summary>坐标来自原生字符矩形；不使用估算列宽处理滚动、折行或制表符。</summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_input is not { IsArrangeValid: true } input || input.Text.Length == 0) return;
        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, Math.Min(ActualHeight, input.ViewportHeight + input.Padding.Top + input.Padding.Bottom))));
        var typeface = new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch);
        var tones = new Dictionary<int, Brush>(); string text = input.Text;
        if (!ReferenceEquals(_text, text)) { _text = text; _starts = LogTextLayout.LineStarts(text); }
        foreach (var line in LogTextLayout.VisibleLines(input, _starts))
        {
            int start = line.Start, end = line.End;
            DrawSelection(drawingContext, input, line);
            if (_numbers is { } numbers && (input.TextWrapping == TextWrapping.NoWrap || start == line.PhysicalStart))
            {
                var number = new FormattedText(line.PhysicalNumber.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(numbers.FontFamily, numbers.FontStyle, numbers.FontWeight, numbers.FontStretch), numbers.FontSize, GutterBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var point = input.TranslatePoint(line.Bounds.TopLeft, this);
                drawingContext.DrawText(number, new Point(52 - number.WidthIncludingTrailingWhitespace, point.Y + (line.Bounds.Height - number.Height) / 2));
            }
            if (end <= start) continue;
            if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1])) start--;
            int maximum = Math.Clamp((int)(ActualWidth / Math.Max(4, input.FontSize * .25)) + 64, 128, 4096);
            end = Math.Min(end, start + maximum);
            if (end < text.Length && end > start && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
            if (!tones.TryGetValue(line.PhysicalStart, out var brush)) tones[line.PhysicalStart] = brush = LineBrush(text.AsSpan(line.PhysicalStart, Math.Min(256, text.Length - line.PhysicalStart)));
            double bodyX = input.TranslatePoint(new Point(2, 0), this).X;
            drawingContext.PushClip(new RectangleGeometry(new Rect(Math.Max(0, bodyX), 0, Math.Max(0, Math.Min(ActualWidth - bodyX, input.ViewportWidth)), ActualHeight)));
            // 每个制表符后的起点由原生控件给出，避免两套排版器的默认 tab 宽度不同。
            while (start < end)
            {
                if (text[start] == '\t') { start++; continue; }
                int tab = text.IndexOf('\t', start, end - start); int segmentEnd = tab < 0 ? end : tab;
                var rect = input.GetRectFromCharacterIndex(start);
                if (rect.IsEmpty) break;
                var origin = input.TranslatePoint(rect.TopLeft, this);
                if (origin.X > ActualWidth) break;
                var formatted = new FormattedText(text[start..segmentEnd], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, input.FontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                // 字符矩形已含原生行距；单行 FormattedText 再指定行高会重复增加顶部留白。
                drawingContext.DrawText(formatted, origin); start = segmentEnd;
            }
            drawingContext.Pop();
        }
        drawingContext.Pop();
    }
    private void DrawSelection(DrawingContext drawing, TextBox input, LogDisplayLine line)
    {
        if (input.SelectionLength == 0) return;
        int from = Math.Max(line.Start, input.SelectionStart), to = Math.Min(line.End, input.SelectionStart + input.SelectionLength);
        if (from >= to) return;
        var point = input.TranslatePoint(line.Bounds.TopLeft, this);
        double right = Math.Min(ActualWidth, input.TranslatePoint(new Point(input.ViewportWidth, 0), this).X);
        // 完整可见行沿用 PEN 的整行底色；部分选择严格使用原生字符矩形，不把命中扩成整行。
        if (from == line.Start && to == line.End) drawing.DrawRectangle(SelectionBrush, null, new Rect(0, point.Y, Math.Max(0, right), line.Bounds.Height));
        else
        {
            var first = input.GetRectFromCharacterIndex(from); var last = input.GetRectFromCharacterIndex(to - 1, true);
            if (first.IsEmpty || last.IsEmpty) return;
            double left = Math.Max(input.TranslatePoint(new Point(2, 0), this).X, input.TranslatePoint(first.TopLeft, this).X);
            right = Math.Min(right, input.TranslatePoint(last.TopRight, this).X);
            if (right > left) drawing.DrawRectangle(SelectionBrush, null, new Rect(left, point.Y, right - left, line.Bounds.Height));
        }
    }
    private Brush LineBrush(ReadOnlySpan<char> header)
    {
        header = header.TrimStart();
        for (int index = 0; index < 4; index++)
        {
            var candidate = header.TrimStart('['); int separator = candidate.IndexOfAny(' ', '\t');
            if (separator < 0) break;
            var token = candidate[..separator];
            bool date = token.Length >= 10 && char.IsDigit(token[0]) && token[4] == '-' && token[7] == '-';
            bool time = token.Length >= 8 && char.IsDigit(token[0]) && token[2] == ':' && token[5] == ':';
            if (!date && !time) break;
            header = candidate[(separator + 1)..].TrimStart();
        }
        header = header.TrimStart('[');
        foreach (string level in new[] { "WARNING", "WARN", "ERROR", "FATAL", "CRITICAL" })
        {
            if (!header.StartsWith(level, StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Length > level.Length && !char.IsWhiteSpace(header[level.Length]) && header[level.Length] is not (']' or ':' or '|')) continue;
            return level is "WARN" or "WARNING" ? WarningBrush : ErrorBrush;
        }
        return BodyBrush;
    }
}
