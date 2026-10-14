using System.Globalization;
using System.Windows.Automation;

namespace Asgard.Hermod.UI.Controls;

/// <summary>按实际字体、DPI 和可用宽度省略中间文字，保留较多尾部用于辨识相似名称。</summary>
public sealed class MiddleEllipsisTextBlock : TextBlock
{
    /// <summary>完整原文不受显示省略影响。</summary>
    public static readonly DependencyProperty FullTextProperty = DependencyProperty.Register(nameof(FullText),
        typeof(string), typeof(MiddleEllipsisTextBlock), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, OnSettingsChanged));

    /// <summary>用于显示、悬浮及无障碍名称的完整原文。</summary>
    public string FullText { get => (string)GetValue(FullTextProperty); set => SetValue(FullTextProperty, value); }

    /// <summary>字体、DPI 或绑定宽度变化时重新计算展示文字。</summary>
    public MiddleEllipsisTextBlock()
    {
        Loaded += (_, _) => RefreshText(MaxWidth);
        SizeChanged += (_, _) => RefreshText(MaxWidth);
    }

    static MiddleEllipsisTextBlock()
    {
        foreach (var property in new[] { MaxWidthProperty, FontFamilyProperty, FontSizeProperty, FontWeightProperty,
            FontStyleProperty, FontStretchProperty, FlowDirectionProperty })
            property.OverrideMetadata(typeof(MiddleEllipsisTextBlock),
                new FrameworkPropertyMetadata(property.GetMetadata(typeof(TextBlock)).DefaultValue, OnSettingsChanged));
    }

    private static void OnSettingsChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var block = (MiddleEllipsisTextBlock)target;
        block.RefreshText(block.MaxWidth);
    }

    private void RefreshText(double width)
    {
        string full = FullText ?? "";
        ToolTip = full;
        AutomationProperties.SetName(this, full);
        Text = Fit(full, width);
    }

    private string Fit(string full, double width)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double Measure(string value) => new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection,
            typeface, FontSize, Foreground ?? Brushes.Black, pixelsPerDip).WidthIncludingTrailingWhitespace;
        if (double.IsPositiveInfinity(width) || Measure(full) <= width) return full;
        if (Measure("…") > width) return "";
        int[] starts = StringInfo.ParseCombiningCharacters(full);
        // 二分测量避免长键产生二次方开销；UTF-16 字符数不能代替文字元素数量。
        string result = "…";
        int low = 1, high = starts.Length - 1;
        while (low <= high)
        {
            int keep = low + (high - low) / 2;
            int head = keep / 3;
            int tail = keep - head;
            string candidate = (head == 0 ? "" : full[..starts[head]]) + "…" + full[starts[starts.Length - tail]..];
            if (Measure(candidate) <= width) { result = candidate; low = keep + 1; }
            else high = keep - 1;
        }
        return result;
    }
}
