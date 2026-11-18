using System.Text;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;

namespace Asgard.Hermod.Mcp;

/// <summary>配置授权时使用固定的工作区和连接版本只读浏览，不依赖已有 MCP 授权。</summary>
internal sealed class McpStorageScopeBrowser(IMcpLocalStore store, IObjectStorageClientFactory factory)
    : IMcpStorageScopeBrowser
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<McpStorageBucket>> ListBucketsAsync(string owner, McpTarget target, CancellationToken cancellationToken = default)
    {
        var identity = Identity(target);
        var connection = Open(owner, identity, cancellationToken);
        using var client = factory.CreateClient(connection);
        var buckets = await client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        Check(owner, identity, cancellationToken);
        return buckets.Where(bucket => ValidBucket(bucket.Name)).Select(bucket => new McpStorageBucket(bucket.Name, bucket.Region))
            .DistinctBy(bucket => bucket.Name, StringComparer.Ordinal).OrderBy(bucket => bucket.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <inheritdoc />
    public async Task<McpStorageFolderPage> ListFoldersAsync(string owner, McpTarget target, McpStorageBucket bucket, string prefix,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        var identity = Identity(target) with { Resource = bucket.Name, Qualifier = prefix };
        McpGrantRules.Validate(identity);
        if (prefix.Length > 0 && !prefix.EndsWith('/') || continuationToken is { Length: 0 or > 8192 }
            || continuationToken?.Any(char.IsControl) == true)
            throw Text.Exception("McpInvalidGrant");
        var connection = Open(owner, identity, cancellationToken);
        using var client = factory.CreateClient(connection, bucket.Region);
        var page = await client.ListObjectsAsync(bucket.Name, prefix, continuationToken, "/", 100, cancellationToken).ConfigureAwait(false);
        Check(owner, identity, cancellationToken);
        if (page.NextToken is { } next && (next.Length is 0 or > 8192 || next.Any(char.IsControl) || next == continuationToken)
            || page.Entries.Any(entry => !entry.Key.StartsWith(prefix, StringComparison.Ordinal)))
            throw Text.Exception("McpStorageDeleteInvalidRange");
        var folders = page.Entries.Where(entry => entry.IsDirectory && entry.Key.Length > prefix.Length && entry.Key.EndsWith('/')
                && !entry.Key[prefix.Length..^1].Contains('/') && McpGrantRules.IsObjectPath(entry.Key)
                && entry.Key.Length <= 1024 && Encoding.UTF8.GetByteCount(entry.Key) <= 1023 && !entry.Key.Any(char.IsControl))
            .Select(entry => entry.Key).Distinct(StringComparer.Ordinal).ToArray();
        return new(folders, page.NextToken);
    }

    private static McpGrant Identity(McpTarget target)
    {
        if (target.TypeId != "storage") throw Text.Exception("McpInvalidGrant");
        return new("", target.Id, target.TypeId, target.Revision, "", "", "", false);
    }

    private InstanceConnectionOptions Open(string owner, McpGrant identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return store.OpenTarget(owner, identity) ?? throw Text.Exception("McpWorkspaceChanged");
    }

    private void Check(string owner, McpGrant identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!store.IsCurrent(owner, identity)) throw Text.Exception("McpWorkspaceChanged");
    }

    private static bool ValidBucket(string bucket)
    {
        try { McpGrantRules.Validate(new("", "", "storage", "", bucket, "", "", false)); return true; }
        catch (Exception) { return false; }
    }
}
