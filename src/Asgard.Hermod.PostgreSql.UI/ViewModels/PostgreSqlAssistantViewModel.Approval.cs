using Asgard.Hermod.Agent;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>用户在原 SQL 卡片审阅与查看结果；确认不跨范围或连接代次。</summary>
public sealed partial class PostgreSqlAssistantViewModel
{
    /// <summary>尚待用户决策的单次操作。</summary>
    public AgentApprovalRequest? PendingApproval { get; private set => SetProperty(ref field, value); }
    private async Task ExecuteSuggestionAsync(PostgreSqlAssistantSql suggestion)
    {
        if (!_useInlineApproval) { await _tab.ExecuteAssistantSqlAsync(suggestion); return; }
        if (_disposed || IsBusy || !_tab.CanRun || !suggestion.Execution.CanExecute) return;
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = "";
        var execution = suggestion.Execution; execution.Begin(); Refresh();
        bool dispatched = false, approved = false;
        try
        {
            using var modelTask = ModelSession?.BeginTask(pending.Token, requireModel: false);
            var taskToken = modelTask?.Token ?? pending.Token;
            await _tab.ExecuteAssistantSqlAsync(suggestion, async () =>
            {
                var approval = new AgentApprovalRequest(Text.Get("ChatReviewOperation"),
                    Text.Format("ChatSqlApprovalTarget", _tab.Instance?.Name ?? "", _tab.Endpoint, _tab.Name, suggestion.Database),
                    suggestion.Sql, taskToken, () => _tab.InsertAssistantSql(suggestion.Sql));
                PendingApproval = approval; Status = Text.Get("ChatAwaitingApproval"); execution.RequestApproval(approval);
                approved = await approval.WaitAsync(); return approved;
            }, taskToken, () =>
            {
                dispatched = true; Status = Text.Get("ChatActionExecuting"); execution.Dispatched();
            }).WaitAsync(taskToken);
            var state = !dispatched ? AgentSqlExecutionState.NotExecuted
                : taskToken.IsCancellationRequested || _tab.OutcomeUnknown ? AgentSqlExecutionState.Unknown
                : _tab.HasError ? AgentSqlExecutionState.Failed : AgentSqlExecutionState.Succeeded;
            // 只冻结本次成功返回的行集，取消或失败时不能误显示之前的工作区结果。
            var result = state == AgentSqlExecutionState.Succeeded ? _tab.Results.FirstOrDefault()?.Result : null;
            string output = result is null ? "" : SqlAgentSample.Create(result.Columns.Select(column => column.Name), result.Rows, result.IsTruncated);
            string detail = dispatched ? Text.Format("ChatSqlExecutionResult", _tab.Status, _tab.Error, _tab.Message)
                : execution.Approval is null && _tab.HasError ? _tab.Error
                : approved ? Text.Get("ChatActionNotExecuted") : execution.Approval?.Status ?? Text.Get("ChatActionNotExecuted");
            execution.Complete(state, detail, output);
        }
        catch (OperationCanceledException)
        {
            execution.Complete(dispatched ? AgentSqlExecutionState.Unknown : AgentSqlExecutionState.NotExecuted);
        }
        catch (Exception error)
        {
            execution.Complete(dispatched ? AgentSqlExecutionState.Unknown : AgentSqlExecutionState.Failed, Text.Error(error, "AgentRequestFailed"));
        }
        finally { PendingApproval = null; _pending = null; IsBusy = false; Status = ""; Refresh(); }
    }
}
