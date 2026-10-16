using System.Diagnostics;
using System.Text;

namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlTabViewModel
{
    /// <summary>仅本页签最近 50 次查询和计划，不写入配置或远程服务。</summary>
    public ObservableCollection<MySqlExecutionTrace> ExecutionTraces { get; } = [];
    /// <summary>当前查看的执行快照，与编辑器的后续改动独立。</summary>
    public MySqlExecutionTrace? SelectedTrace { get; set { if (SetProperty(ref field, value)) RestoreTraceCommand.NotifyCanExecuteChanged(); } }
    /// <summary>是否已有可供检查的执行追踪。</summary>
    public bool HasExecutionTraces => ExecutionTraces.Count > 0;
    /// <summary>计划使用选区或光标所在的一条 SQL；实际分析在运行前单独确认。</summary>
    public async Task ExplainScopeAsync(string selection, int caret, bool analyze)
    {
        if (!CanRun || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        string snapshot = Sql;
        await RunAsync(async token =>
        {
            var session = await SessionAsync(token);
            IReadOnlyList<MySqlStatement> statements;
            if (selection.Length > 0) statements = await session.SplitAsync(selection, token);
            else
            {
                var document = await session.SplitAsync(snapshot, token);
                var current = document.FirstOrDefault(statement => caret >= statement.Start && caret <= statement.Start + statement.Length)
                    ?? (caret >= snapshot.TrimEnd().Length ? document.LastOrDefault() : null);
                statements = current is null ? [] : [current];
            }
            string command = MySqlExecutionPlan.Create(statements, analyze);
            string target = Database; long revision = ScopeRevision;
            if (analyze && !await _dialogs.ConfirmAnalyzeAsync(statements[0].Sql, target, Instance?.Name ?? "", Scope?.SessionName ?? Name, Endpoint)) return;
            token.ThrowIfCancellationRequested();
            if (target != Database || revision != ScopeRevision || !ConnectionAvailable) return;
            var result = await ExecuteTrackedAsync(session, command, analyze ? "analyze" : "explain", token, statements[0].Sql);
            PlanRawText = PlanText(result);
            Error = result.Failure is null ? "" : Text.Error(result.Failure, "MySqlCommandFailed");
            Message = Text.Get(analyze ? "MySqlAnalyzeResultHint" : "MySqlExplainResultHint");
            Status = Text.Format("MySqlExecutionStatus", result.Outcomes.Count, result.Elapsed.TotalMilliseconds);
            OutputView = Error.Length == 0 ? "plan" : "messages";
        });
    }
    private async Task<MySqlExecution> ExecuteTrackedAsync(IMySqlSession session, string sql, string kind, CancellationToken token, string? sourceSql = null)
    {
        var started = DateTimeOffset.Now; var watch = Stopwatch.StartNew(); MySqlExecution? execution = null;
        string detail = "";
        _executed = true;
        try
        {
            execution = await session.ExecuteAsync(sql, Database, token);
            detail = execution.Failure is null ? "" : Text.Error(execution.Failure, "MySqlCommandFailed");
            if (kind != "query") detail = PlanText(execution) + (detail.Length == 0 ? "" : Environment.NewLine + detail);
            return execution;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { detail = Text.Get("MySqlQueryCancelled"); throw; }
        catch (Exception error) { detail = Text.Error(error, "MySqlCommandFailed"); throw; }
        finally
        {
            if (Scope is null) Database = session.CurrentDatabase;
            var trace = new MySqlExecutionTrace(started, kind, Database, sourceSql ?? sql, execution?.Elapsed ?? watch.Elapsed,
                execution?.Results.Sum(result => result.Rows.Count) ?? 0, execution is { Failure: null }, detail);
            ExecutionTraces.Insert(0, trace); while (ExecutionTraces.Count > 50) ExecutionTraces.RemoveAt(ExecutionTraces.Count - 1);
            SelectedTrace = trace; OnPropertyChanged(nameof(HasExecutionTraces));
        }
    }
    private static string PlanText(MySqlExecution execution)
    {
        var result = new StringBuilder();
        foreach (var grid in execution.Results)
        {
            result.AppendLine(string.Join("\t", grid.Columns.Select(column => column.Name)));
            foreach (var row in grid.Rows)
            {
                result.AppendLine(string.Join("\t", row.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))));
            }
        }
        return result.ToString();
    }
}
