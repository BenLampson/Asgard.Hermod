namespace Asgard.Hermod.Ssh;

/// <summary>实例级 SSH 传输边界；首个 Shell 保留兼容接口，新增 PTY 返回独立句柄。</summary>
public interface ISshConnection : ISshConsoleConnection
{
    /// <summary>本次连接核验过的公开主机身份。</summary>
    IReadOnlyList<TrustedHostKey> HostKeys { get; }
    /// <summary>连接与首个 Shell 已一起创建；生产实现返回仅拥有该 Shell 的句柄。</summary>
    ISshConsoleConnection InitialConsole => this;
    /// <summary>共享传输终止信号，与某个 Shell 的退出不同；旧适配器可省略。</summary>
    CancellationToken Disconnected => default;
    /// <summary>在同一已认证传输上新建 PTY；不支持的适配器明确失败。</summary>
    Task<ISshConsoleConnection> OpenConsoleAsync(int columns, int rows, CancellationToken cancellationToken = default)
        => Task.FromException<ISshConsoleConnection>(Text.Exception("SshConsoleUnavailable"));
}
