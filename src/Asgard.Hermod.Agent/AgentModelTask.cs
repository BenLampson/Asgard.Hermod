namespace Asgard.Hermod.Agent;

/// <summary>整个模型和工具循环共用的不可变客户端与取消范围。</summary>
public sealed class AgentModelTask : IDisposable
{
    private readonly Action _release;
    private readonly CancellationTokenSource _source;
    private readonly bool _ownsSource;
    private int _disposed;
    internal AgentModelTask(IAgentClient client, CancellationTokenSource source, Action release, bool ownsSource = true) { Client = client; _source = source; _release = release; _ownsSource = ownsSource; }
    /// <summary>冻结地址、模型和凭据的本次客户端。</summary>
    public IAgentClient Client { get; }
    /// <summary>来源取消、模型切换、配置删除或账号变化均撤销本次任务。</summary>
    public CancellationToken Token => _source.Token;
    /// <inheritdoc />
    public void Dispose() { if (_ownsSource && Interlocked.Exchange(ref _disposed, 1) == 0) { try { _release(); } finally { _source.Dispose(); } } }
}
