namespace Asgard.Hermod.Sync.Tests;

/// <summary>只替代存储边界，支持检查持久化前失败是否保留原会话。</summary>
internal sealed class MemoryAccountSessionStore : IAccountSessionStore
{
    /// <summary>测试会话载荷。</summary>
    public string? Session { get; set; }
    private readonly Dictionary<string, string> _revisions = [];
    /// <inheritdoc />
    public string? LoadSession() => Session;
    /// <inheritdoc />
    public void SaveSession(string? session) => Session = session;
    /// <inheritdoc />
    public string LoadRevision(string owner) => _revisions.GetValueOrDefault(owner, "0");
    /// <inheritdoc />
    public void SaveRevision(string owner, string revision) => _revisions[owner] = revision;
}
