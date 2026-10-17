using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>一次只读工具调用的冻结来源与 SQL；展开结果只查看同次有界证据，不重新查询。</summary>
public sealed class DatabaseAgentEvidenceViewModel(string sql, string target) : ObservableObject
{
    private SqlAgentResult? _result;
    /// <summary>确实发送的原始 SQL，不从回复文本重新提取。</summary>
    public string Sql { get; } = sql;
    /// <summary>发送时冻结的安全实例、Session、数据库与端点。</summary>
    public string Target { get; } = target;
    /// <summary>只读操作的真实阶段。</summary>
    public string Heading => Text.Get(_result is null ? "ChatQueryRunning" : _result.Status == "completed" ? "ChatQueryCompleted" : "SqlAgentQueryFailed");
    /// <summary>服务端已完成请求的指标，不用样本行数冒充总行数。</summary>
    public string Summary => _result?.Metrics is { } metrics ? Text.Format(metrics.IsTruncated ? "ChatToolQueryMetricsTruncated" : "ChatToolQueryMetrics", metrics.Rows, metrics.Columns, metrics.Elapsed.TotalMilliseconds)
        : Text.Get(_result is null ? "ChatToolQueryWaiting" : "ChatToolQueryNoMetrics");
    /// <summary>该次执行返回的原始有界样本或安全错误；仅作为只读文本展示。</summary>
    public string Output => _result?.Output ?? "";
    /// <summary>尚无结果时禁用查看，不能触发服务器请求。</summary>
    public bool HasOutput => Output.Length > 0;
    /// <summary>每条历史证据独立保留结果展开状态。</summary>
    public bool IsResultExpanded { get; set => SetProperty(ref field, value); }
    /// <summary>请求结束只更新同一证据对象，不替换来源与 SQL。</summary>
    public void Complete(SqlAgentResult result)
    {
        if (_result is not null) return;
        _result = result;
        foreach (string name in new[] { nameof(Heading), nameof(Summary), nameof(Output), nameof(HasOutput) }) OnPropertyChanged(name);
    }
}
