using System.Windows.Threading;

namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>连接失效以共享浮层通知；空闲探测只使用元数据会话，不影响查询事务。</summary>
public sealed partial class MySqlWorkspaceViewModel
{
    private readonly Dispatcher? _connectionDispatcher = SynchronizationContext.Current is DispatcherSynchronizationContext ? Dispatcher.CurrentDispatcher : null;
    private CancellationTokenSource? _connectionWatch;
    private Task? _connectionWatchTask;
    private bool _connectionLost;

    private void OnConnectionLost(int generation)
    {
        if (_connectionDispatcher?.CheckAccess() == false) { _connectionDispatcher.BeginInvoke(() => OnConnectionLost(generation)); return; }
        if (IsClosed || IsConnecting || generation != _factory.Generation) return;
        _connectionLost = true; Error = "";
        Connection.Disconnected(Text.Get("MySqlConnectionFailed")); Refresh();
    }
    private void StartConnectionWatch(IMySqlSession session)
    {
        _connectionWatch = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _connectionWatchTask = WatchConnectionAsync(session, _factory.Generation, _connectionWatch.Token);
    }
    private async Task WatchConnectionAsync(IMySqlSession session, int generation, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
                probe.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(session.Options.CommandTimeout, 5, 30)));
                bool alive;
                try { alive = await session.PingAsync(probe.Token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception) { alive = false; }
                if (!ReferenceEquals(_metadata, session) || IsClosed) return;
                if (alive) continue;
                OnConnectionLost(generation); return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task StopConnectionWatchAsync()
    {
        _connectionWatch?.Cancel();
        if (_connectionWatchTask is { } task) await task;
        _connectionWatch?.Dispose(); _connectionWatch = null; _connectionWatchTask = null;
    }
}
