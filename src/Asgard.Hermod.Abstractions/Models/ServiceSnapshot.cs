namespace Asgard.Hermod.Abstractions.Models;

/// <summary>服务详情页面所需的数据；预览快照必须明确标记。</summary>
public sealed record ServiceSnapshot(
    string Name, string Subtitle, string Status, StatusTone StatusTone, bool IsPreview,
    IReadOnlyList<MetricValue> Metrics, IReadOnlyList<ConfigurationValue> Configuration,
    IReadOnlyList<ServiceEvent> Events);
