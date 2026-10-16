using System.Windows.Documents;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace Asgard.Hermod.UI.Controls;

/// <summary>将受限 Markdown 语法树映射成 WPF 文档；链接与图片仅显示文字，不进行导航或网络访问。</summary>
internal static class MarkdownDocumentBuilder
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().DisableHtml().Build();

    /// <summary>保留原文模式的换行，助手模式支持标准段落与管道表格，不使用 HTML 或 XAML 转换。</summary>
    internal static FlowDocument Build(string source, bool markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), ColumnWidth = double.PositiveInfinity, TextAlignment = TextAlignment.Left };
        document.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        document.FontSize = 13;
        document.SetResourceReference(TextElement.ForegroundProperty, "TextBodyBrush");
        if (markdown) AppendBlocks(document.Blocks, Markdown.Parse(source, Pipeline), 0);
        else document.Blocks.Add(new Paragraph(new Run(source)) { Margin = new Thickness(0) });
        return document;
    }

    /// <summary>按解析器已限制嵌套层数的语法节点创建原生块。</summary>
    private static void AppendBlocks(BlockCollection target, ContainerBlock source, int depth)
    {
        foreach (var block in source)
        {
            WpfBlock? rendered = block switch
            {
                HeadingBlock heading => Paragraph(heading, Math.Max(14, 24 - heading.Level * 2), FontWeights.SemiBold),
                ParagraphBlock paragraph => Paragraph(paragraph),
                CodeBlock code => Code(code.Lines.ToString()),
                MdTable table => Table(table, depth + 1),
                ListBlock list => List(list, depth + 1),
                QuoteBlock quote => Quote(quote, depth + 1),
                ThematicBreakBlock => new Paragraph { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 8) },
                _ => null,
            };
            if (rendered is not null)
            {
                if (block is ThematicBreakBlock) rendered.SetResourceReference(WpfBlock.BorderBrushProperty, "LineBrush");
                target.Add(rendered);
            }
            else if (block is ContainerBlock container) AppendBlocks(target, container, depth + 1);
        }
    }

    /// <summary>段落和标题共用行内样式，标题去除 Markdown 标记并保持层级字号。</summary>
    private static Paragraph Paragraph(LeafBlock block, double? size = null, FontWeight? weight = null)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        if (size is { } fontSize) paragraph.FontSize = fontSize;
        if (weight is { } fontWeight) paragraph.FontWeight = fontWeight;
        AppendInlines(paragraph.Inlines, block.Inline, 0);
        return paragraph;
    }

    /// <summary>代码始终作为字面文本显示，不创建可执行控件或修改原有命令确认流程。</summary>
    private static Paragraph Code(string text)
    {
        var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(10), FontSize = 12 };
        paragraph.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "AppBackgroundBrush");
        return paragraph;
    }

    /// <summary>列表使用原生项目符号，并保留有序列表起始序号与嵌套内容。</summary>
    private static System.Windows.Documents.List List(ListBlock source, int depth)
    {
        var list = new System.Windows.Documents.List { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(20, 0, 0, 0), MarkerStyle = source.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };
        if (source.IsOrdered && int.TryParse(source.OrderedStart, System.Globalization.CultureInfo.InvariantCulture, out int start) && start > 0) list.StartIndex = start;
        foreach (var child in source.OfType<ListItemBlock>())
        {
            var item = new ListItem();
            AppendBlocks(item.Blocks, child, depth);
            list.ListItems.Add(item);
        }
        return list;
    }

    /// <summary>引用通过主题色侧边线与缩进区分，内容仍可跨段落选中。</summary>
    private static Section Quote(QuoteBlock source, int depth)
    {
        var section = new Section { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0), Margin = new Thickness(0, 0, 0, 10) };
        section.SetResourceReference(WpfBlock.BorderBrushProperty, "LineBrush");
        AppendBlocks(section.Blocks, source, depth);
        return section;
    }

    /// <summary>表格使用等宽弹性列和自动换行，在窄侧栏中保持完整行列与主题化表头。</summary>
    private static System.Windows.Documents.Table Table(MdTable source, int depth)
    {
        var table = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12) };
        int columns = source.OfType<MdTableRow>().Select(row => row.Count).DefaultIfEmpty().Max();
        for (int index = 0; index < columns; index++) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        foreach (var row in source.OfType<MdTableRow>())
        {
            var rendered = new TableRow();
            if (row.IsHeader)
            {
                rendered.FontWeight = FontWeights.SemiBold;
                rendered.SetResourceReference(TextElement.BackgroundProperty, "AppBackgroundBrush");
            }
            int columnIndex = 0;
            foreach (var cell in row.OfType<MdTableCell>())
            {
                var result = new TableCell { Padding = new Thickness(7, 6, 7, 6), BorderThickness = new Thickness(0.5) };
                result.SetResourceReference(TableCell.BorderBrushProperty, "LineBrush");
                AppendBlocks(result.Blocks, cell, depth);
                foreach (var paragraph in result.Blocks.OfType<Paragraph>()) paragraph.Margin = new Thickness(0);
                if (columnIndex < source.ColumnDefinitions.Count)
                    result.TextAlignment = source.ColumnDefinitions[columnIndex].Alignment switch
                    {
                        Markdig.Extensions.Tables.TableColumnAlign.Center => TextAlignment.Center,
                        Markdig.Extensions.Tables.TableColumnAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left,
                    };
                rendered.Cells.Add(result);
                columnIndex++;
            }
            group.Rows.Add(rendered);
        }
        return table;
    }

    /// <summary>只接受显示节点；HTML、图片地址与链接均不能实例化外部内容或触发系统动作。</summary>
    private static void AppendInlines(InlineCollection target, ContainerInline? source, int depth)
    {
        if (source is null) return;
        foreach (var inline in source)
        {
            WpfInline? rendered = inline switch
            {
                LiteralInline literal => new Run(literal.Content.ToString()),
                CodeInline code => InlineCode(code.Content),
                LineBreakInline line => line.IsHard ? new LineBreak() : new Run(" "),
                EmphasisInline emphasis => Emphasis(emphasis, depth + 1),
                HtmlEntityInline entity => new Run(entity.Transcoded.ToString()),
                AutolinkInline link => new Run(link.Url),
                _ => null,
            };
            if (rendered is not null) target.Add(rendered);
            else if (inline is ContainerInline container) AppendInlines(target, container, depth + 1);
        }
    }

    /// <summary>行内代码通过等宽字体与底色区分，不改变其字面内容。</summary>
    private static Run InlineCode(string text)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.FontFamilyProperty, "MonoFont");
        run.SetResourceReference(TextElement.BackgroundProperty, "AppBackgroundBrush");
        return run;
    }

    /// <summary>嵌套强调映射到粗体、斜体或删除线，其他语义仅保留内部文本。</summary>
    private static Span Emphasis(EmphasisInline source, int depth)
    {
        var span = new Span();
        if (source.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
        else if (source.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
        else span.FontStyle = FontStyles.Italic;
        AppendInlines(span.Inlines, source, depth);
        return span;
    }
}
