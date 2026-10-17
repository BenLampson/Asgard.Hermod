namespace Asgard.Hermod.UI.Controls;

/// <summary>只读文档在原生渲染表面创建后移除插入符留白，使正文、表格和外部标题共用同一起点。</summary>
public class MarkdownDocumentReader : RichTextBox
{
    /// <summary>保留原生选择与复制，默认关闭编辑和文档内交互。</summary>
    public MarkdownDocumentReader() { IsReadOnly = true; IsUndoEnabled = false; IsDocumentEnabled = false; }
    /// <summary>WPF 在 CreateRenderScope 中写入左右五 DIP 页边距，模板接管后恢复阅读布局。</summary>
    public override void OnApplyTemplate() { base.OnApplyTemplate(); Document.PagePadding = new(0); }
}
