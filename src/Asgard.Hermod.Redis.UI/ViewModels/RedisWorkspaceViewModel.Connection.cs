using System.Windows.Threading;

namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>SDK 后台连接与认证失效切回页面线程，保留键页签草稿，关闭弹窗后不重复提示。</summary>
public sealed partial class RedisWorkspaceViewModel
{
    private readonly Dispatcher? _connectionDispatcher = SynchronizationContext.Current is DispatcherSynchronizationContext ? Dispatcher.CurrentDispatcher : null;
    private bool _connectionLost;
    private void OnConnectionLost(object? sender, EventArgs args)
    {
        if (_connectionDispatcher?.CheckAccess() == false) { _connectionDispatcher.BeginInvoke(() => OnConnectionLost(sender, args)); return; }
        if (IsClosed || !ReferenceEquals(sender, _session)) return;
        _connectionLost = true;
        if (IsConnecting) return;
        Error = "";
        Connection.Disconnected(Text.Get("RedisConnectionFailed")); Refresh();
    }
}
