using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>仅保留文本的只读选择复制，图片由本机看图软件打开。</summary>
public partial class TextPreviewWindow : HermodDialog
{
    /// <summary>只接收已经通过大小与文本格式检查的内容。</summary>
    public TextPreviewWindow(FilePreviewContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.IsImage) throw Text.Exception("FilePreviewUnsupported");
        InitializeComponent();
        DataContext = content;
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
    }

    /// <summary>关闭后宿主释放文本预览临时文件。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();
}
