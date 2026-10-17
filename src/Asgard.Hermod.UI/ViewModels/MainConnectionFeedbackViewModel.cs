namespace Asgard.Hermod.UI.ViewModels;

/// <summary>主窗体首次连接的单次反馈；失败后只保留安全来源和显式重试，不保留协议资源。</summary>
public sealed class MainConnectionFeedbackViewModel : ObservableObject
{
    /// <summary>来源在本次连接开始时冻结，回调由工作区协调器验证生命周期。</summary>
    public MainConnectionFeedbackViewModel(InstanceItemViewModel instance, Action cancel, Action retry, Action dismiss)
    {
        Name = instance.Name; Address = instance.Summary.ConnectionAddress;
        Diagnostics = Text.Format("MainConnectionDiagnostics", instance.Id, instance.TypeName, Address);
        CancelCommand = new(_ => { IsCancelling = true; Refresh(); cancel(); }, _ => IsConnecting && !IsCancelling);
        RetryCommand = new(_ => retry(), _ => !IsConnecting && HasError);
        DismissCommand = new(_ => dismiss(), _ => HasError);
    }
    /// <summary>用户创建的来源名称不翻译。</summary>
    public string Name { get; }
    /// <summary>只读取配置目录已经脱敏的地址，不读取连接选项。</summary>
    public string Address { get; }
    /// <summary>诊断仅包含稳定实例 ID、类型和安全地址。</summary>
    public string Diagnostics { get; }
    /// <summary>等待这一次初始化完成。</summary>
    public bool IsConnecting { get; private set; } = true;
    /// <summary>取消按钮等待模块确认释放，不允许重复请求。</summary>
    public bool IsCancelling { get; private set; }
    /// <summary>错误来自模块已经脱敏的本地化消息。</summary>
    public bool HasError => !IsConnecting && Detail.Length > 0;
    /// <summary>整个标题使用句子模板，允许翻译重排实例名称。</summary>
    public string Title => Text.Format(IsConnecting ? "MainConnectionOpening" : "MainConnectionFailed", Name);
    /// <summary>安全说明；不接受原始异常或 SDK 返回。</summary>
    public string Detail { get; private set; } = Text.Get("MainConnectionWaiting");
    /// <summary>仅取消本次请求。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>重新创建已释放的失败页面，不复用故障连接。</summary>
    public RelayCommand RetryCommand { get; }
    /// <summary>回到实例列表，不创建空工作区。</summary>
    public RelayCommand DismissCommand { get; }
    /// <summary>回收完成后转为失败展示，错误正文已经由协议模块处理。</summary>
    internal void Fail(string safeError) { IsConnecting = false; IsCancelling = false; Detail = safeError; Refresh(); }
    /// <summary>结束后的旧按钮不能再影响新连接。</summary>
    internal void Invalidate() { IsConnecting = false; Detail = ""; Refresh(); }
    private void Refresh()
    {
        foreach (string property in new[] { nameof(IsConnecting), nameof(IsCancelling), nameof(HasError), nameof(Title), nameof(Detail) }) OnPropertyChanged(property);
        CancelCommand.NotifyCanExecuteChanged(); RetryCommand.NotifyCanExecuteChanged(); DismissCommand.NotifyCanExecuteChanged();
    }
}
