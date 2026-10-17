namespace Asgard.Hermod.Abstractions.Models;

/// <summary>数据库的稳定身份；实例标识是 Session 范围不可跨越的边界。</summary>
public sealed record DatabaseIdentity(string InstanceId, string Name);
