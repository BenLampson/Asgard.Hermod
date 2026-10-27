using System.Runtime.InteropServices;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>kubectl 页面只处理展示与复制，不向交互终端注入命令。</summary>
public partial class SshKubernetesView : UserControl
{
    /// <summary>加载支持离屏检查的真实资源。</summary>
    public SshKubernetesView() => InitializeComponent();
    /// <summary>关闭只收起页面，查询取消由用户和连接生命周期控制。</summary>
    public event EventHandler? CloseRequested;
    /// <summary>本地查找只改变选区，不重读日志或向 SSH 发送键盘输入。</summary>
    private void OnOutputSearchChanged(object sender, TextChangedEventArgs args) => FindOutput(false);
    /// <summary>搜索快捷键由管理面板处理，不落入右侧 PTY。</summary>
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { OutputSearchTools.Visibility = Visibility.Visible; OutputSearch.Focus(); OutputSearch.SelectAll(); args.Handled = true; }
        else if (args.Key == Key.F3 || args.Key == Key.Enter && ReferenceEquals(args.OriginalSource, OutputSearch))
        { FindOutput(true); args.Handled = true; }
    }
    /// <summary>到达末尾时回到首个匹配，复制仍遵循用户当前选区。</summary>
    private void FindOutput(bool advance)
    {
        if (ResultOutput is null || OutputSearch is null || OutputSearch.Text.Length == 0) return;
        int start = advance ? Math.Min(ResultOutput.Text.Length, ResultOutput.SelectionStart + ResultOutput.SelectionLength) : 0;
        int at = ResultOutput.Text.IndexOf(OutputSearch.Text, start, StringComparison.OrdinalIgnoreCase);
        if (at < 0 && start > 0) at = ResultOutput.Text.IndexOf(OutputSearch.Text, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return;
        ResultOutput.Select(at, OutputSearch.Text.Length); ResultOutput.ScrollToLine(ResultOutput.GetLineIndexFromCharacterIndex(at));
    }
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnCopyOutput(object sender, RoutedEventArgs args)
    {
        string value = ResultOutput.SelectionLength > 0 ? ResultOutput.SelectedText : ResultOutput.Text;
        if (value.Length == 0) return;
        try { Clipboard.SetText(value); }
        catch (ExternalException) { MessageBox.Show(Window.GetWindow(this), Text.Get("DockerLocalActionFailed"), Text.Get("KubeTitle"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
