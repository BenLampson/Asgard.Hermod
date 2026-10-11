using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>使用共享窗口外观显示操作失败信息，支持确认或通过 Escape 关闭。</summary>
public partial class MessageDialog : HermodDialog
{
    /// <summary>填充窗口标题和用户可读的错误信息。</summary>
    public MessageDialog(string title, string message)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    /// <summary>确认信息后结束模态对话框。</summary>
    private void OnDismissClick(object sender, RoutedEventArgs args) => DialogResult = true;
}
