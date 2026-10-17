using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Services;

/// <summary>窗口关闭仅隐藏；应用退出时统一释放，来源关闭不抹去记录。</summary>
public sealed class AgentWindowService : IAgentWindowService, IDisposable, IAsyncDisposable
{
    private readonly Dictionary<object, AgentChatWindow> _windows = new(ReferenceEqualityComparer.Instance);
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _disposed;
    /// <inheritdoc />
    public void Open(AgentWindowRequest request)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        if (!_windows.TryGetValue(request.Source, out var window))
        {
            window = new(request); _windows.Add(request.Source, window);
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show(); window.Activate();
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.Invoke(Dispose); return; }
        if (_disposed) return;
        _disposed = true;
        var windows = _windows.Values.ToArray();
        _windows.Clear();
        foreach (var window in windows) window.ClosePermanently();
    }

    /// <summary>DI 的异步释放可能切换线程；所有助手窗口必须在创建它们的 Dispatcher 上关闭。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_dispatcher.CheckAccess()) Dispose();
        else await _dispatcher.InvokeAsync(Dispose);
    }
}
