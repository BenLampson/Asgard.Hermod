namespace Asgard.Hermod.Ssh;

/// <summary>Docker 镜像列表的一行；名称、标签和时间保持服务端原值。</summary>
public sealed record SshDockerImageSnapshot(string Repository, string Tag, string Id, string Created, string Size)
{
    /// <summary>列表使用短标识，完整标识仍可用于提示与搜索。</summary>
    public string ShortId => Id.StartsWith("sha256:", StringComparison.Ordinal) && Id.Length > 19 ? Id[7..19] : Id[..Math.Min(12, Id.Length)];
}
