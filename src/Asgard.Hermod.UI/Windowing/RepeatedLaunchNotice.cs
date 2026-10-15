using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>原实例负责重复启动提示；合并重复请求，并等待现有业务模态输入结束。</summary>
public sealed class RepeatedLaunchNotice : IDisposable
{
    private readonly Window _owner;
    private readonly Action _activate;
    private readonly Func<bool> _canShow;
    private readonly Func<Window?> _findModal;
    private readonly Action<MessageDialog> _show;
    private Window? _deferredModal;
    private bool _showing;
    private bool _disposed;

    /// <summary>沿用当前实例的语言和窗口归属，第二个进程不创建数据库、托盘或额外窗口。</summary>
    public RepeatedLaunchNotice(Window owner, Action activate, Func<bool> canShow)
        : this(owner, activate, canShow, WindowActivation.FindModal, dialog => dialog.ShowDialog()) { }

    /// <summary>以可替换的显示边界验证重入和延迟提示，测试不需要显示桌面窗口。</summary>
    internal RepeatedLaunchNotice(Window owner, Action activate, Func<bool> canShow, Func<Window?> findModal, Action<MessageDialog> show)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(activate); ArgumentNullException.ThrowIfNull(canShow);
        ArgumentNullException.ThrowIfNull(findModal); ArgumentNullException.ThrowIfNull(show);
        _owner = owner; _activate = activate; _canShow = canShow; _findModal = findModal; _show = show;
        owner.Closed += OnOwnerClosed;
    }

    /// <summary>每次先唤回现有窗口；已有提示时只激活，不堆叠多个对话框。</summary>
    public void Notify()
    {
        if (_disposed || !_canShow()) return;
        _activate();
        if (_showing || _disposed || !_canShow()) return;
        if (_findModal() is { } modal)
        {
            if (_deferredModal is null) { _deferredModal = modal; modal.Closed += OnModalClosed; }
            return;
        }
        _showing = true;
        var dialog = MessageDialog.ForAlreadyRunning();
        try
        {
            if (_owner.IsVisible) dialog.Owner = _owner;
            _show(dialog);
        }
        finally { dialog.Close(); _showing = false; }
    }

    /// <summary>结束业务模态后再显示状态说明，不替换用户尚未完成的输入。</summary>
    private void OnModalClosed(object? sender, EventArgs args)
    {
        if (_deferredModal is not null) _deferredModal.Closed -= OnModalClosed;
        _deferredModal = null;
        if (!_owner.Dispatcher.HasShutdownStarted) _owner.Dispatcher.BeginInvoke(Notify);
    }

    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();

    /// <summary>退出或关闭后移除延迟事件，排队的重复启动不能重新显示窗口。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _owner.Closed -= OnOwnerClosed;
        if (_deferredModal is not null) _deferredModal.Closed -= OnModalClosed;
        _deferredModal = null;
    }
}
