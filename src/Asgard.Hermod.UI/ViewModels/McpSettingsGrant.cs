namespace Asgard.Hermod.UI.ViewModels;

/// <summary>设置页只显示现有授权的范围和是否失效，不展示配对密钥。</summary>
public sealed record McpSettingsGrant(string Id, string Name, string Scope, bool IsCurrent);
