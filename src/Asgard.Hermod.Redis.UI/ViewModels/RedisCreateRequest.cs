namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>新键的最小初始值，只在不存在时提交。</summary>
public sealed record RedisCreateRequest(string Key, string Type, string Field, string Value, string Score);
