namespace Asgard.Hermod.UI.Controls;

/// <summary>下拉选项保留原始对象，筛选和键盘候选不会写回已确认的选择。</summary>
public sealed record SelectOption(object Value, string Label, bool IsCommitted);
