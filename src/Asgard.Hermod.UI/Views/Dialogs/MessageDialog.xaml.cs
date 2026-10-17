using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>使用共享窗口显示错误、警告或状态说明，支持确认及 Escape 关闭。</summary>
public partial class MessageDialog : HermodDialog
{
    /// <summary>默认保持原有错误外观，非错误调用方必须明确提供标题及语义类别。</summary>
    public MessageDialog(string title, string message, string? heading = null, MessageDialogKind kind = MessageDialogKind.Error)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        HeadingText.Text = heading ?? Text.Get("ActionFailed");
        Kind = kind;
        StatusIcon.Kind = kind == MessageDialogKind.Information ? "info" : "triangle-alert";
        StatusIcon.SetResourceReference(ForegroundProperty, kind switch
        {
            MessageDialogKind.Information => "AccentBrush",
            MessageDialogKind.Warning => "WarningBrush",
            _ => "DangerBrush",
        });
    }

    /// <summary>稳定的提示类别，测试与辅助功能无需比较翻译文本。</summary>
    public MessageDialogKind Kind { get; }

    /// <summary>托盘不可用只说明功能降级，不声称整个应用操作失败。</summary>
    public static MessageDialog ForTrayUnavailable() => new(Text.Get("TrayUnavailableTitle"), Text.Get("TrayUnavailable"), Text.Get("TrayUnavailableTitle"), MessageDialogKind.Warning);

    /// <summary>重复启动由原实例显示，沿用其已经应用的语言偏好。</summary>
    public static MessageDialog ForAlreadyRunning() => new(Text.Get("HermodAlreadyRunningTitle"), Text.Get("HermodAlreadyRunningMessage"), Text.Get("HermodAlreadyRunningTitle"), MessageDialogKind.Information);

    /// <summary>确认信息后结束模态对话框。</summary>
    private void OnDismissClick(object sender, RoutedEventArgs args) => DialogResult = true;
}
