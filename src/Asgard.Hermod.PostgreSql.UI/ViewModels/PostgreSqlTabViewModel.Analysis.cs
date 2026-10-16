using System.Diagnostics;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>显式分析选中内容或光标语句；普通 EXPLAIN 不执行目标语句。</summary>
public sealed partial class PostgreSqlTabViewModel
{
    /// <summary>只保留本页签最近 50 次执行追踪，不持久化 SQL 或查询结果。</summary>
    public ObservableCollection<PostgreSqlExecutionTrace> ExecutionTraces { get; } = [];
    /// <summary>当前查看的执行快照不随编辑器后续改动变化。</summary>
    public PostgreSqlExecutionTrace? SelectedTrace { get; set { if (SetProperty(ref field, value)) RestoreTraceCommand.NotifyCanExecuteChanged(); } }
    /// <summary>只有实际执行后才显示追踪区域。</summary>
    public bool HasExecutionTraces => ExecutionTraces.Count > 0;
    /// <summary>服务器返回的执行计划与结果使用相同的有界表格展示。</summary>
    public async Task ExplainScopeAsync(string selection, int caret)
    {
        if (!CanRun || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        string snapshot = Sql;
        await RunAsync(async token =>
        {
            var session = await SessionAsync(token);
            string sql = selection;
            if (sql.Length == 0)
            {
                var statements = await session.SplitAsync(snapshot, token);
                var current = statements.FirstOrDefault(statement => caret >= statement.Start && caret <= statement.Start + statement.Length)
                    ?? (caret >= snapshot.TrimEnd().Length ? statements.LastOrDefault() : null);
                sql = current?.Sql ?? "";
            }
            if (string.IsNullOrWhiteSpace(sql)) return;
            if (Plan is not null) Plan.IsStale = true;
            PostgreSqlExecution execution;
            try { execution = await ExecuteTrackedAsync(session, sql, true, token); }
            catch { OutputView = "messages"; throw; }
            if (execution.Failure is null && ExecutionTraces.FirstOrDefault()?.Plan is { } snapshotPlan) Plan = new(snapshotPlan);
            Error = execution.Failure is null ? "" : Text.Error(execution.Failure, "PostgreSqlCommandFailed");
            Message = execution.Failure is null ? Text.Get("PostgreSqlExplainComplete") : "";
            Status = Text.Format("PostgreSqlExecutionStatus", execution.Outcomes.Count, execution.Elapsed.TotalMilliseconds);
            OutputView = Error.Length == 0 ? "plan" : "messages";
        });
    }

    private async Task<PostgreSqlExecution> ExecuteTrackedAsync(IPostgreSqlSession session, string sql, bool explain, CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow; var watch = Stopwatch.StartNew(); PostgreSqlExecution? execution = null; string detail = ""; string target = Database; DatabaseExecutionPlan? plan = null;
        _executed = true;
        try
        {
            execution = explain ? await session.ExplainAsync(sql, target, token) : await session.ExecuteAsync(sql, target, token);
            detail = execution.Failure is null ? "" : Text.Error(execution.Failure, "PostgreSqlCommandFailed");
            if (explain) { plan = PostgreSqlPlanReader.Read(execution, sql, target); detail = plan.RawText + (detail.Length == 0 ? "" : Environment.NewLine + detail); }
            return execution;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { detail = Text.Get("PostgreSqlQueryCancelled"); throw; }
        catch (Exception error) { detail = Text.Error(error, "PostgreSqlCommandFailed"); throw; }
        finally
        {
            var trace = new PostgreSqlExecutionTrace(started, explain ? "explain" : "query", target, sql, execution?.Elapsed ?? watch.Elapsed,
                execution?.Results.Sum(result => result.Rows.Count) ?? 0, execution is { Failure: null }, detail)
            { Plan = plan };
            ExecutionTraces.Insert(0, trace); while (ExecutionTraces.Count > 50) ExecutionTraces.RemoveAt(ExecutionTraces.Count - 1);
            SelectedTrace = trace; OnPropertyChanged(nameof(HasExecutionTraces));
        }
    }

}
