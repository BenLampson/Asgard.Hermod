using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>将后台流快照顺序送到创建它的界面线程；释放后拒绝迟到更新。</summary>
public sealed class AgentResponseStream<T>(Func<T> create, Action<T> add) : IDisposable where T : AgentDisplayMessage
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private T? _current;
    private bool _closed;

    /// <summary>等待界面应用本次快照，取消后排队的回调不会创建消息。</summary>
    public async ValueTask ReceiveAsync(AgentStreamUpdate update, CancellationToken token)
    {
        void Apply()
        {
            token.ThrowIfCancellationRequested();
            if (_closed) return;
            if (_current is null)
            {
                if (update.Content.Length == 0 && update.Thinking.Length == 0) return;
                _current = create(); _current.ApplyStream(update); add(_current);
            }
            else _current.ApplyStream(update);
        }
        if (_dispatcher.CheckAccess()) Apply();
        else await _dispatcher.InvokeAsync(Apply, DispatcherPriority.Background, token);
    }

    /// <summary>一个工具回合结束后允许下一个回合创建新回复，绝不重建已经展示的消息。</summary>
    public void Complete(T final, AgentReply reply)
    {
        _dispatcher.VerifyAccess();
        if (_closed) return;
        if (_current is null) { final.CompleteStream(final, reply); add(final); }
        else _current.CompleteStream(final, reply);
        _current = null;
    }

    /// <summary>取消和错误保留收到的内容，停止扫光，不安装未完成的执行建议。</summary>
    public void Dispose()
    {
        _dispatcher.VerifyAccess(); _closed = true; _current?.InterruptStream(); _current = null;
    }
}
