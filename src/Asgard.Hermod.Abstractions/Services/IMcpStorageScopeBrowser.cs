namespace Asgard.Hermod.Abstractions.Services;

/// <summary>在用户配置授权时只读浏览指定版本的对象存储，不创建 MCP 授权。</summary>
public interface IMcpStorageScopeBrowser
{
    /// <summary>读取当前工作区指定实例的可见 Bucket。</summary>
    Task<IReadOnlyList<McpStorageBucket>> ListBucketsAsync(string owner, McpTarget target, CancellationToken cancellationToken = default);

    /// <summary>按页读取指定目录的直接子文件夹。</summary>
    Task<McpStorageFolderPage> ListFoldersAsync(string owner, McpTarget target, McpStorageBucket bucket, string prefix,
        string? continuationToken = null, CancellationToken cancellationToken = default);
}
