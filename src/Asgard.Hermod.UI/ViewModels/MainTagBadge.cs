namespace Asgard.Hermod.UI.ViewModels;

/// <summary>标签只按内置稳定身份表达生产警示，用户文字不参与行为判断。</summary>
public sealed record MainTagBadge(string Label, string Hint, bool IsProduction);
