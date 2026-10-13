namespace Asgard.Hermod.Abstractions.Models;

/// <summary>复合条件使用稳定字段值，翻译后的标签不参与适用性判断。</summary>
public sealed record InstanceFieldCondition(string Key, string Value);
