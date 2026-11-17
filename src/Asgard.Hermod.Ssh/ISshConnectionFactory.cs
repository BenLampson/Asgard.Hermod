namespace Asgard.Hermod.Ssh;

/// <summary>为测试或终端打开独立 SSH 会话，连接配置只能在协议层解读。</summary>
public interface ISshConnectionFactory
{
    /// <summary>先核验身份与认证，再分配交互式 PTY，不执行自定义命令。</summary>
    Task<ISshConnection> OpenAsync(InstanceConnectionOptions options, int columns, int rows, CancellationToken cancellationToken = default);

    /// <summary>在已信任的连接上运行调用方提供的固定诊断，不提示新增主机信任。</summary>
    Task<SshCommandResult> ExecuteDiagnosticAsync(InstanceConnectionOptions options, string command, CancellationToken cancellationToken = default)
        => Task.FromException<SshCommandResult>(Text.Exception("SshMonitorUnavailable"));
}
