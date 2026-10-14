using Asgard.Hermod.UI.Controls;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>承载真实模块工作页；无 Owner，主窗体隐藏不会连带隐藏工作窗口。</summary>
public sealed class WorkspaceWindow : HermodWindow
{
    private readonly WorkspaceViewModel _workspace;
    private readonly WorkspaceContentHost _host;
    private bool _closing;

    /// <summary>复用页面模板和会话所有权，不复制模块内容或创建托盘。</summary>
    public WorkspaceWindow(WorkspaceViewModel workspace, WorkspacePageViewModel page)
    {
        _workspace = workspace;
        Page = page;
        DataContext = page;
        SetBinding(TitleProperty, new Binding(nameof(WorkspacePageViewModel.Header)));
        Width = 1200; Height = 800; MinWidth = 800; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        _host = new WorkspaceContentHost { Page = page };
        Content = _host;
        Closing += OnClosing;
        page.PropertyChanged += OnPageChanged;
    }

    /// <summary>窗口终生持有同一个页面及其草稿和连接。</summary>
    public WorkspacePageViewModel Page { get; }
    /// <summary>全应用退出预检期间阻止逐窗关闭，避免局部资源提前释放。</summary>
    public bool IsExitPending { get; set; }
    /// <summary>尚未得到模块关闭许可时，全应用退出不能再启动第二轮预检。</summary>
    public bool IsClosePending => _closing && !Page.IsClosed;

    /// <summary>按真实显示器 DPI 限制默认窗口尺寸。</summary>
    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        WindowStartupPlacement.Apply(this);
    }

    /// <summary>单窗关闭沿用模块既有确认，失败保留原窗口。</summary>
    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (Page.IsClosed) return;
        args.Cancel = true;
        if (_closing || IsExitPending) return;
        _closing = true;
        try { await _workspace.CloseTabAsync(Page); }
        catch (Exception error)
        {
            new MessageDialog(Text.Get("Hermod"), Text.Error(error)) { Owner = this }.ShowDialog();
        }
        finally { _closing = false; }
    }

    /// <summary>资源释放通知晚于 Closing 返回，避免同步关闭重入。</summary>
    private void OnPageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspacePageViewModel.IsClosed) && Page.IsClosed)
            Dispatcher.BeginInvoke(async () =>
            {
                IsEnabled = false;
                try { await Page.CloseAsync(); }
                finally { Close(); }
            });
    }

    /// <summary>视图仅在所属窗口终止时释放，隐藏主窗体不经过这里。</summary>
    protected override void OnClosed(EventArgs args)
    {
        Page.PropertyChanged -= OnPageChanged;
        _host.Dispose();
        base.OnClosed(args);
    }
}
