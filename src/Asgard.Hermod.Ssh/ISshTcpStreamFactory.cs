namespace Asgard.Hermod.Ssh;

/// <summary>供其他协议复用 SSH 认证与本机主机信任，不分配终端或执行命令。</summary>
public interface ISshTcpStreamFactory
{
    /// <summary>返回拥有 SSH 客户端的 TCP 转发流，关闭流同时释放连接。</summary>
    Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default);
}
