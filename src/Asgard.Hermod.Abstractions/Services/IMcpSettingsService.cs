namespace Asgard.Hermod.Abstractions.Services;

/// <summary>MCP 设置管理；所有更改立即生效并撤销现有 MCP 会话。</summary>
public interface IMcpSettingsService
{
    /// <summary>读取当前工作区的授权和可选实例。</summary>
    Task<McpSettingsSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>启动或关闭访问；关闭后废止此前发放的配对凭证。</summary>
    Task<McpSettingsSnapshot> SetEnabledAsync(string owner, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>为指定版本的实例添加范围授权，授权标识由服务分配。</summary>
    Task<McpSettingsSnapshot> AddGrantAsync(string owner, McpGrant grant, CancellationToken cancellationToken = default);

    /// <summary>撤销指定授权，允许移除已失效的记录。</summary>
    Task<McpSettingsSnapshot> RemoveGrantAsync(string owner, string grantId, CancellationToken cancellationToken = default);

    /// <summary>替换本机禁止范围；所有未列出的实例与位置默认可请求访问。</summary>
    Task<McpSettingsSnapshot> SetExclusionsAsync(string owner, IReadOnlyList<McpExclusion> exclusions, CancellationToken cancellationToken = default);

    /// <summary>由桌面设置勾选或取消指定连接版本的持久授权，禁止范围仍然生效。</summary>
    Task<McpSettingsSnapshot> SetInstanceApprovalAsync(string owner, McpInstanceApproval approval, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>核对实例版本，并在同一次保存中更新整实例禁用与持久授权，保留细粒度禁止位置。</summary>
    Task<McpSettingsSnapshot> SetInstancePolicyAsync(string owner, McpInstanceApproval approval, McpInstancePolicy selection, CancellationToken cancellationToken = default);

    /// <summary>仅响应明确的复制动作返回含配对凭证的客户端配置。</summary>
    Task<string> GetClientConfigurationAsync(string owner, CancellationToken cancellationToken = default);
}
