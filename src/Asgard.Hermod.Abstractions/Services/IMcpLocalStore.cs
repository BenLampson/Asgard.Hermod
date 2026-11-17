namespace Asgard.Hermod.Abstractions.Services;

/// <summary>本地策略与连接版本的受保护存储，不参与账号同步。</summary>
public interface IMcpLocalStore
{
    /// <summary>读取当前工作区及其连接版本快照。</summary>
    McpWorkspace ReadWorkspace();

    /// <summary>读取活动工作区标识。</summary>
    string ReadOwner();

    /// <summary>读取当前用户加密保存的配对信息和授权。</summary>
    McpLocalPolicy? ReadPolicy();

    /// <summary>仅当工作区仍匹配时保存策略，避免用户操作跨越切换边界。</summary>
    void SavePolicy(McpLocalPolicy policy);

    /// <summary>从同一快照核对工作区及连接版本，成功后才解密该连接。</summary>
    InstanceConnectionOptions? OpenTarget(string owner, McpGrant grant);

    /// <summary>发回结果前检查连接归属及版本仍然有效，无需解密。</summary>
    bool IsCurrent(string owner, McpGrant grant);
}
