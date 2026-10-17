namespace Asgard.Hermod.UI.Common;

/// <summary>将同步界面操作绑定为 WPF 命令。</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    /// <summary>创建操作和可选的启用条件。</summary>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>启用条件变化后刷新按钮。</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>查询当前参数是否可以执行。</summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>只执行满足启用条件的操作。</summary>
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            _execute(parameter);
    }

    /// <summary>主动刷新命令的启用状态。</summary>
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
