namespace Asgard.Hermod.Abstractions.Models;

/// <summary>冲突界面只接收安全预览，参数值始终遮蔽。</summary>
public sealed record ConfigurationConflict(string Key, string EntityName, string Field,
    string LocalValue, string CloudValue);
