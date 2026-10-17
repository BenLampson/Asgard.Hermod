namespace Asgard.Hermod.UI.Models;

/// <summary>阅读器模式使用稳定 ID，不能用翻译后的标签判断行为。</summary>
public sealed record DatabasePlanMode(string Id, string Name, bool IsEnabled, string Icon);
