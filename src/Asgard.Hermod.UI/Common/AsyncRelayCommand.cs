namespace Asgard.Hermod.UI.Common;

/// <summary>串行执行异步界面操作，保存失败信息并通知宿主。</summary>
public sealed class AsyncRelayCommand : ObservableObject, ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly bool _disableWhileRunning;
    private bool _isRunning;

    /// <summary>创建异步操作和可选的启用条件；模态入口可保持启用外观，执行入口仍阻止重入。</summary>
    public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null, bool disableWhileRunning = true)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
        _disableWhileRunning = disableWhileRunning;
    }

    /// <summary>命令启用条件改变时通知绑定。</summary>
    public event EventHandler? CanExecuteChanged;
    /// <summary>将异常交给宿主显示，不让 async void 异常终止应用。</summary>
    public event EventHandler<Exception>? ExecutionFailed;
    /// <summary>最近一次执行的异常；开始下一次执行时清空。</summary>
    public Exception? LastException { get; private set; }
    /// <summary>当前是否仍在执行。</summary>
    public bool IsRunning => _isRunning;

    /// <summary>按业务条件和运行状态决定绑定控件是否启用。</summary>
    public bool CanExecute(object? parameter) => (!_disableWhileRunning || !_isRunning) && (_canExecute?.Invoke(parameter) ?? true);

    /// <summary>提供 ICommand 的异步入口。</summary>
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    /// <summary>提供可等待的入口，便于宿主协调与验证。</summary>
    public async Task ExecuteAsync(object? parameter = null)
    {
        // 模态窗口已阻断用户输入，允许保留按钮外观，但程序调用仍不能重复打开。
        if (_isRunning || !CanExecute(parameter))
            return;
        _isRunning = true;
        LastException = null;
        OnPropertyChanged(nameof(IsRunning));
        NotifyCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception exception)
        {
            LastException = exception;
            OnPropertyChanged(nameof(LastException));
            ExecutionFailed?.Invoke(this, exception);
        }
        finally
        {
            _isRunning = false;
            OnPropertyChanged(nameof(IsRunning));
            NotifyCanExecuteChanged();
        }
    }

    /// <summary>在条件变化后主动更新命令。</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
