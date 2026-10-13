namespace Asgard.Hermod.Abstractions.Models;

/// <summary>仅保存实例标识与访问时间，不包含名称、地址或凭据。</summary>
public sealed record RecentInstance(string InstanceId, DateTimeOffset OpenedAt);
