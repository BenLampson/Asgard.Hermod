namespace Asgard.Hermod.Abstractions.Models;

/// <summary>仅保存实例标识与访问时间，不包含名称、地址或凭据。</summary>
public sealed record RecentInstance(string InstanceId, DateTimeOffset OpenedAt)
{
    /// <summary>首页与本机持久化共用六条访问记录上限。</summary>
    public const int MaximumCount = 6;
}
