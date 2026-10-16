using Asgard.Hermod.MySql.UI.Services;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>四类输出共享一个槽；视图切换不执行 SQL，也不改变历史快照。</summary>
public sealed partial class MySqlTabViewModel
{
    /// <summary>服务器的完整计划原文，读取和切换视图不会重新执行。</summary>
    public string PlanRawText => Plan?.RawText ?? "";
    /// <summary>当前计划的独立阅读状态，失败请求不替换上次快照。</summary>
    public DatabaseExecutionPlanViewModel? Plan
    {
        get; private set
        {
            if (!SetProperty(ref field, value)) return;
            foreach (string name in new[] { nameof(HasPlan), nameof(PlanRawText), nameof(OutputRequestSummary), nameof(OutputFooterText), nameof(OutputFooterHint) }) OnPropertyChanged(name);
        }
    }
    /// <summary>计划为空时显示本地化入口说明。</summary>
    public bool HasPlan => Plan is not null;
    /// <summary>请求耗时与当前输出相符，不套用另一个结果的耗时。</summary>
    public string OutputRequestSummary => IsPlanOutput ? Plan?.RequestTime ?? "" : IsResultsOutput ? QuerySummary : IsMessagesOutput && HasError ? Text.Format("DatabaseMessageErrorCount", 1) : "";
    /// <summary>计划保留底部阅读提示，但不重复结果操作。</summary>
    public bool IsOutputFooterVisible => IsResultsOutput || IsPlanOutput || IsMessagesOutput;
    /// <summary>结果和消息各自可以复制或导出当前本地输出。</summary>
    public bool IsOutputActionsVisible => IsResultsOutput || IsMessagesOutput;
    /// <summary>互斥输出的底部说明。</summary>
    public string OutputFooterText => IsMessagesOutput ? MessageFooter : IsPlanOutput ? Plan?.FooterHint ?? Text.Get("PlanNoPlan") : OutputSummary;
    /// <summary>状态说明与当前输出匹配。</summary>
    public string OutputFooterHint => IsMessagesOutput ? Text.Get("DatabaseMessagePositionSafety") : IsPlanOutput ? Plan?.MetricNotice ?? Text.Get("PlanNoPlanHint") : EditHint;
    /// <summary>本地化的四个输出入口，行为只比较稳定 ID。</summary>
    public IReadOnlyList<DatabaseOutputOption> OutputViews { get; } = [
        new("results", Text.Get("DatabaseOutputResults"), "table-2"), new("messages", Text.Get("DatabaseOutputMessages"), "message-square"),
        new("plan", Text.Get("DatabaseOutputPlan"), "git-branch"), new("history", Text.Get("DatabaseOutputHistory"), "history")];
    /// <summary>互斥输出视图。</summary>
    public string OutputView
    {
        get; set
        {
            if (value is not ("results" or "messages" or "plan" or "history") || !SetProperty(ref field, value)) return;
            foreach (string name in new[] { nameof(IsResultsOutput), nameof(IsMessagesOutput), nameof(IsPlanOutput), nameof(IsHistoryOutput), nameof(IsHistoryOpen), nameof(IsOutputFooterVisible), nameof(IsOutputActionsVisible), nameof(OutputRequestSummary), nameof(OutputFooterText), nameof(OutputFooterHint) }) OnPropertyChanged(name);
        }
    } = "results";
    /// <summary>显示结果。</summary>
    public bool IsResultsOutput => OutputView == "results";
    /// <summary>显示消息与 SQL 错误。</summary>
    public bool IsMessagesOutput => OutputView == "messages";
    /// <summary>显示计划。</summary>
    public bool IsPlanOutput => OutputView == "plan";
    /// <summary>显示当前 Session 的最近五十次执行。</summary>
    public bool IsHistoryOutput => OutputView == "history";
    /// <summary>结果页脚仅说明已加载范围；编辑条件通过提示按需查看。</summary>
    public string OutputSummary => SelectedResult is { } result ? Text.Format(result.IsEditable ? "DatabaseEditableRows" : "DatabaseReadOnlyRows", result.Rows.Count) : "";
    /// <summary>请求耗时独立于计划节点的服务器时间。</summary>
    private TimeSpan? _resultElapsed;
    /// <summary>结果保留产生它的请求耗时，执行计划和历史选择不会覆盖它。</summary>
    public string QuerySummary => SelectedResult is { } result ? _resultElapsed is { } elapsed
        ? Text.Format("DatabaseQuerySummary", result.Rows.Count, result.Columns.Count, elapsed.TotalMilliseconds) : result.Summary : "";
    /// <summary>恢复 SQL 只替换编辑器草稿，保存或取消均由原有确认边界处理。</summary>
    public AsyncRelayCommand RestoreTraceCommand => field ??= new(async _ =>
    {
        if (SelectedTrace is not { } trace || IsBusy) return;
        if (IsDocumentDirty)
        {
            var choice = await _dialogs.ResolveDraftAsync(Header, true);
            if (choice == MySqlDraftChoice.Cancel) return;
            if (choice == MySqlDraftChoice.Save) { await SaveDocumentAsync(); if (IsDocumentDirty) return; }
        }
        Sql = trace.Sql;
    }, _ => SelectedTrace is not null && !IsBusy);
}
