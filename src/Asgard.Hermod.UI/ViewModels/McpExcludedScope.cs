namespace Asgard.Hermod.UI.ViewModels;

/// <summary>禁止列表的只读展示，移除操作仍使用稳定的规则标识。</summary>
public sealed record McpExcludedScope(string Id, string Instance, string Scope);
