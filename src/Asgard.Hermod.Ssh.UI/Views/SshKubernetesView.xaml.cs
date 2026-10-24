using System.Runtime.InteropServices;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>kubectl 页面只处理展示与复制，不向交互终端注入命令。</summary>
public partial class SshKubernetesView : UserControl
{
    /// <summary>加载支持离屏检查的真实资源。</summary>
    public SshKubernetesView() => InitializeComponent();
    /// <summary>关闭只收起页面，查询取消由用户和连接生命周期控制。</summary>
    public event EventHandler? CloseRequested;
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnCopyOutput(object sender, RoutedEventArgs args)
    {
        string value = ResultOutput.SelectionLength > 0 ? ResultOutput.SelectedText : ResultOutput.Text;
        if (value.Length == 0) return;
        try { Clipboard.SetText(value); }
        catch (ExternalException) { MessageBox.Show(Window.GetWindow(this), Text.Get("DockerLocalActionFailed"), Text.Get("KubeTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
