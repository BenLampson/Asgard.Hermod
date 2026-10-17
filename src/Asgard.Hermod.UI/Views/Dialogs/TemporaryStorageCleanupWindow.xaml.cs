using Asgard.Hermod.UI.Controls;
using Asgard.Hermod.UI.Localization;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>只收集清理确认，不在窗体内删除文件；预计值来自最新的真实扫描。</summary>
public partial class TemporaryStorageCleanupWindow : HermodDialog
{
    /// <summary>确认文本使用完整句子模板，支持翻译重新排列容量参数。</summary>
    public TemporaryStorageCleanupWindow(long estimatedBytes)
    {
        InitializeComponent();
        EstimateText.Text = Text.Format("StorageConfirmCleanupBody", FileSizeText.Format(estimatedBytes));
    }
    /// <summary>主动点击清理才授权执行。</summary>
    private void OnCleanupClick(object sender, RoutedEventArgs args) => DialogResult = true;
    /// <summary>取消和 Escape 都不清理任何文件。</summary>
    private void OnCancelClick(object sender, RoutedEventArgs args) => DialogResult = false;
}
