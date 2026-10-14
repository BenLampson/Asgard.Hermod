namespace Asgard.Hermod.Abstractions.Models;

/// <summary>仅保存实例标识与访问时间，不包含名称、地址或凭据。</summary>
public sealed record RecentInstance(string InstanceId, DateTimeOffset OpenedAt)
{
    /// <summary>首页仅展示六条，持久化保留每个实例的最后访问时间。</summary>
    public const int MaximumCount = 6;
}
