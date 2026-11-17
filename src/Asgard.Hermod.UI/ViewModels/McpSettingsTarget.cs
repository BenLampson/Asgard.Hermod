namespace Asgard.Hermod.UI.ViewModels;

/// <summary>为授权下拉框提供可辨认名称，同时保留固定连接版本。</summary>
public sealed record McpSettingsTarget(McpTarget Target, string Label);
