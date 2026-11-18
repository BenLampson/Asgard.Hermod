namespace Asgard.Hermod.Abstractions.Models;

/// <summary>本机客户端访问一个实例时采用的互斥策略，禁止位置始终优先。</summary>
public enum McpInstancePolicy
{
    /// <summary>每次连接首次使用该实例时确认。</summary>
    Confirm,
    /// <summary>允许已配对客户端直接访问此连接版本。</summary>
    AlwaysAllow,
    /// <summary>隐藏实例并拒绝所有资源请求。</summary>
    Blocked,
}
