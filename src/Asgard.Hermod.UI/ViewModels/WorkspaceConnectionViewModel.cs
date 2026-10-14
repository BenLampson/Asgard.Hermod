namespace Asgard.Hermod.UI.ViewModels;

/// <summary>协议无关的连接浮层状态；错误必须先由模块转换为安全本地化说明。</summary>
public sealed class WorkspaceConnectionViewModel : ObservableObject
{
    private Action? _cancel;
    /// <summary>创建取消和关闭动作，不持有协议连接。</summary>
    public WorkspaceConnectionViewModel()
    {
        CancelCommand = new RelayCommand(_ => { IsCancelling = true; Refresh(); _cancel?.Invoke(); }, _ => IsConnecting && !IsCancelling);
        DismissCommand = new RelayCommand(_ => { IsOpen = false; Refresh(); }, _ => !IsConnecting);
    }
    /// <summary>是否正在等待本次连接完成。</summary>
    public bool IsConnecting { get; private set; }
    /// <summary>取消已请求，等待协议释放连接。</summary>
    public bool IsCancelling { get; private set; }
    /// <summary>浮层覆盖页面，不参与页面正文的布局测量。</summary>
    public bool IsOpen { get; private set; }
    /// <summary>安全且已经本地化的连接失败说明。</summary>
    public string Error { get; private set; } = "";
    /// <summary>失败状态在关闭浮层后仍保留。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>加载、取消和失败的完整本地化标题。</summary>
    public string Title => Text.Get(IsCancelling ? "WorkspaceConnectionCancelling" : IsConnecting ? "WorkspaceConnectionConnecting" : "WorkspaceConnectionFailed");
    /// <summary>加载说明或安全失败内容。</summary>
    public string Detail => IsConnecting ? Text.Get("WorkspaceConnectionWaiting") : Error;
    /// <summary>取消只影响调用者提供的本次连接请求。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>关闭失败浮层，保留错误状态及外部重试入口。</summary>
    public RelayCommand DismissCommand { get; }
    /// <summary>模块开始连接时绑定本次取消动作并清除上次错误。</summary>
    public void Begin(Action cancel)
    { _cancel = cancel; IsConnecting = true; IsCancelling = false; IsOpen = true; Error = ""; Refresh(); }
    /// <summary>成功或取消隐藏浮层；失败留在同一位置展示安全说明。</summary>
    public void Finish(string error = "")
    { _cancel = null; IsConnecting = false; IsCancelling = false; Error = error; IsOpen = HasError; Refresh(); }
    private void Refresh()
    {
        foreach (string property in new[] { nameof(IsConnecting), nameof(IsCancelling), nameof(IsOpen), nameof(Error), nameof(HasError), nameof(Title), nameof(Detail) }) OnPropertyChanged(property);
        CancelCommand.NotifyCanExecuteChanged(); DismissCommand.NotifyCanExecuteChanged();
    }
}
