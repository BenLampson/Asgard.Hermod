namespace Asgard.Hermod.UI.Tests;

/// <summary>保留真实终端缓冲区，同时用可控任务代替进程退出与连接释放。</summary>
internal sealed class FakeTerminalWorkspaceSession : ITerminalWorkspaceSession
{
    private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposalGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _disposeTask;

    /// <summary>根据用例需求暂停异步释放，便于检查关闭顺序。</summary>
    internal FakeTerminalWorkspaceSession(bool holdDisposal)
    {
        if (!holdDisposal)
            _disposalGate.SetResult();
    }

    /// <inheritdoc />
    public TerminalSession Terminal { get; } = new();

    /// <inheritdoc />
    public string DisplayName => "Test PowerShell";

    /// <inheritdoc />
    public Task<int> Completion => _completion.Task;

    /// <summary>记录应用实际发起释放的次数。</summary>
    internal int DisposeCalls { get; private set; }

    /// <summary>说明异步释放及真实缓冲区回收已经完成。</summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>在异步等待之前观察终端视图是否已经解绑。</summary>
    internal Action? OnDisposalStarting { get; set; }

    /// <summary>模拟正常退出，保留缓冲区直到工作页关闭。</summary>
    internal void Complete(int exitCode = 0) => _completion.TrySetResult(exitCode);

    /// <summary>允许连接释放继续执行。</summary>
    internal void ReleaseDisposal() => _disposalGate.TrySetResult();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        if (_disposeTask is null)
        {
            OnDisposalStarting?.Invoke();
            _disposeTask = FinishDisposalAsync();
        }
        return new ValueTask(_disposeTask);
    }

    /// <summary>等待可控的连接清理门后回收真正的终端会话。</summary>
    private async Task FinishDisposalAsync()
    {
        await _disposalGate.Task;
        Terminal.Dispose();
        IsDisposed = true;
        _completion.TrySetResult(0);
    }
}
