namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>用户审阅完整操作后才复用原 Session 执行；确认不跨范围或连接代次。</summary>
public sealed partial class MySqlAssistantViewModel
{
    /// <summary>尚待用户决策的单次操作。</summary>
    public AgentApprovalRequest? PendingApproval { get; private set => SetProperty(ref field, value); }
    private async Task ExecuteSuggestionAsync(MySqlAssistantSql suggestion)
    {
        if (!_useInlineApproval) { await _tab.ExecuteAssistantSqlAsync(suggestion); return; }
        if (_disposed || IsBusy || !_tab.CanRun) return;
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = ""; Refresh();
        MySqlAssistantMessage? record = null; bool dispatched = false;
        try
        {
            using var modelTask = ModelSession?.BeginTask(pending.Token, requireModel: false);
            var taskToken = modelTask?.Token ?? pending.Token;
            await _tab.ExecuteAssistantSqlAsync(suggestion, async () =>
            {
                var approval = new AgentApprovalRequest(Text.Get("ChatReviewOperation"),
                    Text.Format("ChatSqlApprovalTarget", _tab.Instance?.Name ?? "", _tab.Endpoint, _tab.Name, suggestion.Database),
                    suggestion.Sql, taskToken, () => _tab.InsertAssistantSql(suggestion.Sql));
                PendingApproval = approval; Status = Text.Get("ChatAwaitingApproval");
                record = new("", false, isTool: true, approval: approval); record.Update("", "ChatAwaitingApproval"); AddMessage(record);
                return await approval.WaitAsync();
            }, taskToken, () =>
            {
                dispatched = true; Status = Text.Get("ChatActionExecuting"); record?.Update(suggestion.Sql, "ChatActionExecuting");
            }).WaitAsync(taskToken);
            if (record is not null)
            {
                string state = !dispatched ? "ChatActionNotExecuted" : taskToken.IsCancellationRequested || _tab.OutcomeUnknown ? "ChatActionUnknown" : _tab.HasError ? "ChatActionFailed" : "ChatActionCompleted";
                record.Update(Text.Format("ChatSqlActionResult", suggestion.Database, suggestion.Sql, dispatched ? _tab.Status : Text.Get("ChatActionNotExecuted"), dispatched ? _tab.Error : "", dispatched ? _tab.Message : ""), state);
            }
            else if (_tab.HasError && !_disposed) AddFailure("", _tab.Error);
        }
        catch (OperationCanceledException) { record?.Update(suggestion.Sql, dispatched ? "ChatActionUnknown" : "ChatActionNotExecuted"); }
        catch (Exception error)
        {
            record?.Update(suggestion.Sql, dispatched ? "ChatActionUnknown" : "ChatActionNotExecuted");
            if (!_disposed) AddFailure("", Text.Error(error, "AgentRequestFailed"));
        }
        finally { PendingApproval = null; _pending = null; IsBusy = false; Status = ""; Refresh(); }
    }
}
