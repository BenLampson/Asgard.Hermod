namespace Asgard.Hermod.UI.Models;

/// <summary>连接测试中可展示的只读检查结果。</summary>
public sealed record InstanceConnectionCheck(string Name, bool IsSuccessful);
