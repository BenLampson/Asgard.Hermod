namespace Asgard.Hermod.Abstractions.Models;

/// <summary>连接测试中可展示的单项验证结果。</summary>
public sealed record InstanceConnectionCheck(string Name, bool IsSuccessful);
