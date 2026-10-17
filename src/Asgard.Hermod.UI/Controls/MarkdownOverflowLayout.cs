using System.Windows.Documents;

namespace Asgard.Hermod.UI.Controls;

/// <summary>只替换程序生成的已知文档节点，不解析 HTML、XAML 或模型提供的控件描述。</summary>
internal static class MarkdownOverflowLayout
{
    internal static void Wrap(BlockCollection blocks, MarkdownView owner, double indent = 0)
    {
        foreach (var block in blocks.ToArray())
        {
            BlockUIContainer? replacement = null;
            if (block is Table table)
            {
                var marker = new Paragraph(); blocks.InsertBefore(block, marker); blocks.Remove(block);
                replacement = new(new MarkdownOverflowBlock(table, owner, indent)); blocks.InsertBefore(marker, replacement); blocks.Remove(marker);
            }
            else if (block.GetValue(MarkdownOverflowBlock.CodeTextProperty) is string code)
            {
                replacement = new(new MarkdownOverflowBlock(code, owner, indent)); blocks.InsertBefore(block, replacement); blocks.Remove(block);
            }
            else if (block is Section section) Wrap(section.Blocks, owner, indent + 24);
            else if (block is System.Windows.Documents.List list) foreach (var item in list.ListItems) Wrap(item.Blocks, owner, indent + 24);
            if (replacement is not null) replacement.Margin = new(0, 0, 0, 12);
        }
    }
}
