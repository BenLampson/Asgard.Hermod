using System.Windows.Data;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>容器日志使用真正的非模态窗口，拥有任务栏入口及独立最大化、调整大小能力。</summary>
public sealed class SshContainerLogWindow : HermodWindow
{
    private readonly SshContainerLogView _view;
    /// <summary>连接由工作区拥有，关闭日志窗口只释放读取和本窗口的 AI 请求。</summary>
    public SshContainerLogWindow(SshContainerLogViewModel document)
    {
        Document = document; DataContext = document;
        SetBinding(TitleProperty, new Binding(nameof(SshContainerLogViewModel.Title)));
        Width = 1440; Height = 900; MinWidth = 980; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = true; CaptionHeight = 48;
        CaptionContent = new SshLogCaption { DataContext = document };
        Content = _view = new SshContainerLogView { DataContext = document };
    }
    /// <summary>固定文档身份，外部选中行不改变目标。</summary>
    public SshContainerLogViewModel Document { get; }
    /// <summary>读取不会在构造或离屏验收期间启动，只在实际显示后开始。</summary>
    protected override void OnContentRendered(EventArgs args)
    {
        base.OnContentRendered(args);
        if (Document.CapturedAt is null && Document.RefreshCommand.CanExecute(null)) Document.RefreshCommand.Execute(null);
    }
    /// <summary>按真实显示器工作区限制初始尺寸。</summary>
    protected override void OnSourceInitialized(EventArgs args) { base.OnSourceInitialized(args); WindowStartupPlacement.Apply(this); }
    /// <summary>关闭后取消本窗口的读取和分析，不关闭 SSH 连接。</summary>
    protected override void OnClosed(EventArgs args) { _view.Dispose(); Document.Dispose(); base.OnClosed(args); }
}
