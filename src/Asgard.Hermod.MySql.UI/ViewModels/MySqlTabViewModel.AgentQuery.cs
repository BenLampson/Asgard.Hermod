using Asgard.Hermod.Agent;

namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlTabViewModel
{
    /// <summary>独立连接只读查询，不继承、提交或回滚查询分析器中用户自己的事务。</summary>
    internal async Task<SqlAgentResult> ExecuteAgentReadOnlyAsync(SqlAgentQuery query, long revision, object identity, CancellationToken cancellationToken)
    {
        void EnsureCurrent()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || !ConnectionAvailable || IsManagingScope || revision != ScopeRevision || query.Database != Database
                || Scope is not null && !Scope.Contains(query.Database) || !ReferenceEquals(identity, AssistantSessionIdentity))
                throw Text.Exception("MySqlAssistantContextChanged");
        }
        EnsureCurrent();
        ReadOnlySqlQuery.Validate(query.Sql);
        if (!CanRun || IsBusy) throw Text.Exception("MySqlAssistantSessionBusy");
        if (CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return new("denied", Text.Get("SqlAgentDraftRetained"), true);
        EnsureCurrent();
        SqlAgentResult? result = null;
        await RunAsync(async token =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var values = new Dictionary<string, string>(_options.Parameters, StringComparer.Ordinal) { ["database"] = query.Database };
            await using var session = await _factory.OpenAsync(new InstanceConnectionOptions { TypeId = _options.TypeId, ProviderId = _options.ProviderId, Parameters = values }, timeout.Token);
            EnsureCurrent();
            var execution = await session.ExecuteReadOnlyAsync(query.Sql, query.Database, timeout.Token);
            EnsureCurrent();
            SetResults(execution.Results.Select(item => item with { EditableTable = null }), execution.Elapsed);
            Error = execution.Failure is null ? "" : Text.Error(execution.Failure, "MySqlCommandFailed");
            Status = Text.Format("MySqlExecutionStatus", execution.Outcomes.Count, execution.Elapsed.TotalMilliseconds);
            var grid = execution.Results.FirstOrDefault();
            string sample = grid is null ? "{}" : SqlAgentSample.Create(grid.Columns.Select(column => column.Name), grid.Rows, grid.IsTruncated);
            bool succeeded = execution.Failure is null && execution.Outcomes.Count > 0 && execution.Outcomes.All(outcome => outcome.Succeeded);
            result = new(succeeded ? "completed" : "failed", succeeded ? sample : Error.Length > 0 ? Error : Text.Get("SqlAgentQueryFailed"), !succeeded);
            var trace = new MySqlExecutionTrace(DateTimeOffset.UtcNow, "agent-query", query.Database, query.Sql, execution.Elapsed, execution.Results.Sum(item => item.Rows.Count), succeeded, Error);
            ExecutionTraces.Insert(0, trace); while (ExecutionTraces.Count > 50) ExecutionTraces.RemoveAt(ExecutionTraces.Count - 1);
            SelectedTrace = trace; OnPropertyChanged(nameof(HasExecutionTraces));
        });
        EnsureCurrent();
        return result ?? new("unknown", Text.Get("SqlAgentQueryFailed"), true);
    }
}
