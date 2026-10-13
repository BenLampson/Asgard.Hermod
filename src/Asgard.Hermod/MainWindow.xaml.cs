using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod;

/// <summary>
/// 承载应用外壳，窗口外观与原生行为由共享组件负责；关闭前等待终端清理。
/// </summary>
public partial class MainWindow : HermodWindow
{
    private readonly ShellViewModel _model;
    private bool _closing;
    private bool _closeApproved;

    /// <summary>
    /// 初始化窗口。
    /// </summary>
    public MainWindow(ShellViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.Sidebar.AddInstanceCommand.ExecutionFailed += OnCommandFailed;
        model.Workspace.CloseTabCommand.ExecutionFailed += OnCommandFailed;
        Closing += OnWindowClosing;
    }

    /// <summary>WPF 已选择启动显示器并取得真实 DPI 后，限制默认尺寸，首次显示即完整可见。</summary>
    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        if (WindowStartupLocation == WindowStartupLocation.CenterScreen)
            WindowStartupPlacement.Apply(this);
    }

    /// <summary>实际交互失败时由宿主提供用户可见反馈。</summary>
    private void OnCommandFailed(object? sender, Exception exception)
    {
        var dialog = new MessageDialog(Text.Get("Hermod"), Text.Error(exception)) { Owner = this };
        dialog.ShowDialog();
    }

    /// <summary>保持 Dispatcher 可用，等待 ConPTY 输出收尾后再真正关闭。</summary>
    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        if (_closeApproved)
            return;
        args.Cancel = true;
        if (_closing)
            return;
        _closing = true;
        ShellView.IsEnabled = false;
        try
        {
            await _model.DisposeAsync();
        }
        catch (Exception exception)
        {
            OnCommandFailed(this, exception);
        }
        finally
        {
            _model.Sidebar.AddInstanceCommand.ExecutionFailed -= OnCommandFailed;
            _model.Workspace.CloseTabCommand.ExecutionFailed -= OnCommandFailed;
            ShellView.Dispose();
            _closeApproved = true;
            // 等当前 Closing 事件返回后再关闭，避免同步清理时重入窗口关闭流程。
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
