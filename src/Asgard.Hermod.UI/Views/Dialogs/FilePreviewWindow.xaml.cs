using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>图片可适应窗口和缩放，文本只读并支持原生选择复制。</summary>
public partial class FilePreviewWindow : HermodDialog
{
    /// <summary>只接收已经完成大小限制和解码检查的内容。</summary>
    public FilePreviewWindow(FilePreviewContent content)
    {
        InitializeComponent();
        DataContext = content;
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
    }

    /// <summary>关闭后宿主释放预览临时文件。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();
    /// <summary>恢复适应窗口。</summary>
    private void OnFitClick(object sender, RoutedEventArgs args) => ZoomSlider.Value = 1;
    /// <summary>缩放只改变显示尺寸，原始文件不被修改。</summary>
    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> args) => UpdateImageSize();
    /// <summary>窗口尺寸变化后重新计算适应尺寸。</summary>
    private void OnImageSizeChanged(object sender, SizeChangedEventArgs args) => UpdateImageSize();
    /// <summary>以视口边界为基准，放大时滚动查看图片。</summary>
    private void UpdateImageSize()
    {
        if (ImageViewport is null || ImageContent is null) return;
        ImageContent.Width = Math.Max(1, ImageViewport.ActualWidth - 20) * ZoomSlider.Value;
        ImageContent.Height = Math.Max(1, ImageViewport.ActualHeight - 20) * ZoomSlider.Value;
    }
}
