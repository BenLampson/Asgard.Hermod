namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>SSH 只提供终端上下文与业务动作，共享面板负责消息、输入和滚动。</summary>
public partial class SshAssistantView : UserControl
{
    /// <summary>要求宿主隐藏分屏，不关闭或清空对话。</summary>
    public event EventHandler? CloseRequested;
    /// <summary>用户明确附加终端选区。</summary>
    public event EventHandler? AttachSelectionRequested;
    /// <summary>加载共享聊天面板与 SSH 专用插槽。</summary>
    public SshAssistantView() => InitializeComponent();

    /// <summary>转发共享面板的收起请求。</summary>
    private void OnClose(object? sender, EventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    /// <summary>转发用户明确附加选区的操作。</summary>
    private void OnAttachSelection(object sender, RoutedEventArgs args) => AttachSelectionRequested?.Invoke(this, EventArgs.Empty);
}
