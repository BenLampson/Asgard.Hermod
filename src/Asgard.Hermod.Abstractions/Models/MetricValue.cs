namespace Asgard.Hermod.Abstractions.Models;

/// <summary>一个指标卡片的标签、格式化数值和图标。</summary>
public sealed record MetricValue(string Label, string Value, string Icon);
