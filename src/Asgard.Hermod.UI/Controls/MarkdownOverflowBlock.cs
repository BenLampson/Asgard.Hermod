using System.Globalization;
using System.Windows.Automation;
using System.Windows.Documents;

namespace Asgard.Hermod.UI.Controls;

/// <summary>代码和表格各自拥有原生选区和横向滚动，模型内容只能进入只读文本控件。</summary>
public sealed class MarkdownOverflowBlock : Border
{
    internal static readonly DependencyProperty CodeTextProperty = DependencyProperty.RegisterAttached("CodeText", typeof(string), typeof(MarkdownOverflowBlock));
    /// <summary>代码原文的原生只读控件；表格块为空。</summary>
    public TextBox? CodeInput { get; }
    /// <summary>独立表格文档；代码块为空。</summary>
    public FlowDocument? TableDocument { get; }

    internal MarkdownOverflowBlock(string code, MarkdownView owner, double indent)
    {
        SetWidth(owner, indent); CornerRadius = new(6); SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        CodeInput = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            IsUndoEnabled = false,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new(0),
            Padding = new(12),
            FontSize = 12,
            Background = Brushes.Transparent
        };
        AutomationProperties.SetName(CodeInput, Text.Get("ChatCodeBlock"));
        CodeInput.SetResourceReference(Control.FontFamilyProperty, "MonoFont"); CodeInput.SetResourceReference(Control.ForegroundProperty, "TextBodyBrush");
        TextBlock.SetLineHeight(CodeInput, 21); TextBlock.SetLineStackingStrategy(CodeInput, LineStackingStrategy.BlockLineHeight); Child = CodeInput;
    }
    internal MarkdownOverflowBlock(Table table, MarkdownView owner, double indent)
    {
        SetWidth(owner, indent); CornerRadius = new(6); BorderThickness = new(1); SetResourceReference(BorderBrushProperty, "LineBrush");
        double minimum = 0;
        var mono = owner.TryFindResource("MonoFont") as FontFamily ?? owner.FontFamily;
        for (int index = 0; index < table.Columns.Count; index++)
        {
            double width = 88;
            foreach (var row in table.RowGroups.SelectMany(group => group.Rows))
            {
                if (row.Cells.Count <= index) continue;
                string text = new TextRange(row.Cells[index].ContentStart, row.Cells[index].ContentEnd).Text.Trim();
                if (text.Length > 512) text = text[..512];
                var measured = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, Brushes.Black, VisualTreeHelper.GetDpi(owner).PixelsPerDip);
                width = Math.Max(width, Math.Min(320, measured.WidthIncludingTrailingWhitespace + 24));
            }
            minimum += width;
            bool numeric = table.RowGroups.SelectMany(group => group.Rows).Any(row => row.Cells.Count > index && row.Cells[index].TextAlignment == TextAlignment.Right);
            table.Columns[index].Width = numeric ? new(width) : new(1, GridUnitType.Star);
        }
        table.Margin = new(0); table.FontSize = 12; table.LineHeight = 20; table.FontFamily = mono;
        TableDocument = new FlowDocument(table) { PagePadding = new(0), ColumnWidth = double.PositiveInfinity, PageWidth = minimum };
        TableDocument.SetResourceReference(TextElement.ForegroundProperty, "TextBodyBrush");
        var reader = new MarkdownDocumentReader
        {
            Document = TableDocument,
            IsReadOnly = true,
            IsUndoEnabled = false,
            IsDocumentEnabled = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new(0),
            Padding = new(0),
            Background = Brushes.Transparent
        };
        AutomationProperties.SetName(reader, Text.Get("ChatTableBlock"));
        Child = reader; TableDocument.PagePadding = new(0);
        SizeChanged += (_, _) => TableDocument.PageWidth = Math.Max(minimum, ActualWidth - BorderThickness.Left - BorderThickness.Right);
    }
    private void SetWidth(MarkdownView owner, double indent)
    {
        ClipToBounds = true;
        SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = owner, Converter = new MarkdownViewportWidthConverter(indent) });
    }
}
