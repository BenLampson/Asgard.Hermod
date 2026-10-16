using System.Diagnostics;
using System.Text.Json;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>每条 SQL 建议保留自己的确认和结果；更新原卡片，不向消息集合追加审批记录。</summary>
public sealed class AgentSqlSuggestionExecution : ObservableObject
{
    private readonly Stopwatch _elapsed = new();
    /// <summary>当前阶段只使用稳定枚举。</summary>
    public AgentSqlExecutionState State { get; private set; }
    /// <summary>本次单独授权的冻结目标，重跑会创建新请求。</summary>
    public AgentApprovalRequest? Approval { get; private set; }
    /// <summary>曾经派发过 SQL 时，入口明确显示再次执行。</summary>
    public bool HasExecuted { get; private set; }
    /// <summary>当前操作未结束时隐藏重复入口。</summary>
    public bool IsActive => State is AgentSqlExecutionState.AwaitingApproval or AgentSqlExecutionState.Executing;
    /// <summary>只在待决策阶段展示确认动作。</summary>
    public bool IsAwaitingApproval => State == AgentSqlExecutionState.AwaitingApproval;
    /// <summary>未知结果不得通过原建议直接重试。</summary>
    public bool CanExecute => !IsActive && State != AgentSqlExecutionState.Unknown;
    /// <summary>最新一次操作的安全说明，按值保存，不跟随工作区后续查询。</summary>
    public string Result { get; private set; } = "";
    /// <summary>同次执行首个结果集的有界快照，不会为了展示重新查询。</summary>
    public string Output { get; private set; } = "";
    /// <summary>存在结果时才展示阅读区域。</summary>
    public bool HasResult => Result.Length > 0;
    /// <summary>没有行集时不展示样本区域。</summary>
    public bool HasOutput => Output.Length > 0;
    /// <summary>等待用户确认的时间不计入执行耗时。</summary>
    public string Elapsed => !IsActive && HasExecuted && _elapsed.ElapsedTicks > 0 ? Text.Format("ChatActivityElapsed", _elapsed.Elapsed.TotalMilliseconds) : "";
    /// <summary>成功文案与重跑入口独立，失败不会被标成已执行成功。</summary>
    public string Status => State switch
    {
        AgentSqlExecutionState.AwaitingApproval => Text.Get("ChatAwaitingApproval"),
        AgentSqlExecutionState.Executing => Text.Get("ChatActionExecuting"),
        AgentSqlExecutionState.Succeeded => Text.Get("ChatSqlExecuted"),
        AgentSqlExecutionState.NotExecuted => Text.Get("ChatActionNotExecuted"),
        AgentSqlExecutionState.Failed => Text.Get("ChatActionFailed"),
        AgentSqlExecutionState.Unknown => Text.Get("ChatActionUnknown"),
        _ => "",
    };
    /// <summary>准备新一轮，不继承旧审批权限或旧结果。</summary>
    public void Begin()
    {
        Approval = null; Result = ""; Output = ""; _elapsed.Reset();
        State = AgentSqlExecutionState.AwaitingApproval; Refresh();
    }
    /// <summary>审批对象归属原建议，并保持原 Session 冻结目标。</summary>
    public void RequestApproval(AgentApprovalRequest approval)
    {
        Approval = approval; Refresh();
    }
    /// <summary>仅实际派发时记录执行，不能把点击审批入口算作执行。</summary>
    public void Dispatched()
    {
        HasExecuted = true; State = AgentSqlExecutionState.Executing; _elapsed.Start(); Refresh();
    }
    /// <summary>冻结本轮结果，迟到回调不得覆盖已经结束的记录。</summary>
    public void Complete(AgentSqlExecutionState state, string result = "", string output = "")
    {
        if (!IsActive) return;
        State = state; Result = result; Output = FormatOutput(output); _elapsed.Stop(); Refresh();
    }
    /// <summary>将已生成的有界样本排成只读文本表格，不向用户展示模型协议字段。</summary>
    private static string FormatOutput(string sample)
    {
        if (sample.Length == 0) return "";
        using var document = JsonDocument.Parse(sample);
        if (!document.RootElement.TryGetProperty("columns", out var columns) || !document.RootElement.TryGetProperty("rows", out var rows)) return "";
        string Cell(JsonElement value) => (value.GetString() ?? "").Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
        string[] headings = columns.EnumerateArray().Select(Cell).ToArray();
        if (headings.Length == 0) return "";
        var values = rows.EnumerateArray().Select(row => row.EnumerateArray().Select(Cell).ToArray()).Prepend(headings).ToArray();
        int[] widths = Enumerable.Range(0, headings.Length).Select(index => values.Max(row => index < row.Length ? row[index].Length : 0)).ToArray();
        return string.Join(Environment.NewLine, values.Select(row => string.Join(" | ", widths.Select((width, index) => (index < row.Length ? row[index] : "").PadRight(width)))));
    }
    /// <summary>一次更新通知所有派生显示属性，保持卡片对象和阅读位置。</summary>
    private void Refresh()
    {
        foreach (string name in new[] { nameof(State), nameof(Approval), nameof(HasExecuted), nameof(IsActive), nameof(IsAwaitingApproval), nameof(CanExecute), nameof(Result), nameof(Output), nameof(HasResult), nameof(HasOutput), nameof(Elapsed), nameof(Status) }) OnPropertyChanged(name);
    }
}
