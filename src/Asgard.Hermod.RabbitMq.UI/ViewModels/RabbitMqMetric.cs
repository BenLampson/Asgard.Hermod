namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>统计卡片只呈现服务器返回的值。</summary>
public sealed record RabbitMqMetric(string Label, string Value, string Hint = "");
