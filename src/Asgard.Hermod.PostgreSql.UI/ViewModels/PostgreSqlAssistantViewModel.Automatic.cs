using Asgard.Hermod.Agent;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>SQL 自动模式只提供只读查询闭环，手动执行和修改确认继续复用原有逻辑。</summary>
public sealed partial class PostgreSqlAssistantViewModel
{
    /// <summary>默认手动；进行中的任务不允许改变授权模式。</summary>
    public bool IsAutomaticExecution
    {
        get;
        set
        {
            if (_disposed || IsBusy || !SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(ExecutionMode));
        }
    }

    /// <summary>底部模式选择使用稳定 ID，不比较本地化文本。</summary>
    public string ExecutionMode
    {
        get => IsAutomaticExecution ? "automatic" : "manual";
        set { if (value is "automatic" or "manual") IsAutomaticExecution = value == "automatic"; }
    }

    /// <summary>模式列表在构造视图前按当前资源语言生成。</summary>
    public IReadOnlyList<InstanceFieldChoice> ExecutionModes { get; } =
        [new("manual", Text.Get("SqlAgentManual")), new("automatic", Text.Get("SqlAgentAutomatic"))];

    /// <summary>任务结束后才允许切换执行模式，防止中途扩张授权。</summary>
    public bool CanChangeExecutionMode => !IsBusy && !_disposed;

    private async Task SendAutomaticAsync()
    {
        if (_conversation is null) { Error = Text.Get("AgentConfigurationRequired"); return; }
        string prompt = Prompt;
        using var pending = new CancellationTokenSource();
        _pending = pending; IsBusy = true; Error = ""; Refresh();
        LastFailedPrompt = ""; Status = Text.Get("ChatPreparingContext"); AddMessage(new(prompt, true)); Prompt = "";
        try
        {
            using var modelTask = ModelSession?.BeginTask(pending.Token);
            var taskToken = modelTask?.Token ?? pending.Token;
            long revision = _tab.ScopeRevision;
            var context = await _tab.ReadAssistantContextAsync(IncludeSql, IncludeResults, taskToken);
            object identity = _tab.AssistantSessionIdentity;
            void EnsureCurrent()
            {
                taskToken.ThrowIfCancellationRequested();
                if (_disposed || !IsAutomaticExecution || context.Database != _tab.Database || revision != _tab.ScopeRevision || !ReferenceEquals(identity, _tab.AssistantSessionIdentity) || !_tab.ConnectionAvailable)
                    throw Text.Exception("PostgreSqlAssistantContextChanged");
            }
            EnsureCurrent();
            Status = Text.Get("ChatWaitingModel");
            await _conversation.RunAsync(prompt, context, async (query, token) =>
            {
                EnsureCurrent();
                var record = new PostgreSqlAssistantMessage(Text.Format("SqlAgentStepQuery", query.Database, query.Sql), false, isTool: true);
                Status = Text.Get("ChatReadingData");
                record.Update(record.Content, "SqlAgentQueryRunning"); AddMessage(record);
                try
                {
                    var result = await _tab.ExecuteAgentReadOnlyAsync(query, revision, identity, token);
                    EnsureCurrent();
                    record.Update(Text.Format("SqlAgentStepResult", query.Database, query.Sql, result.Output), result.Status == "completed" ? "SqlAgentQueryCompleted" : "SqlAgentQueryStopped");
                    Status = Text.Get("ChatWaitingModel");
                    return result;
                }
                catch (OperationCanceledException) { record.Update(record.Content, "SqlAgentQueryStopped"); throw; }
                catch (Exception error)
                {
                    EnsureCurrent();
                    string safe = Text.Error(error, "SqlAgentQueryFailed");
                    record.Update(Text.Format("SqlAgentStepResult", query.Database, query.Sql, safe), "SqlAgentQueryStopped");
                    return new SqlAgentResult("unknown", safe, true);
                }
            }, reply =>
            {
                EnsureCurrent();
                AddMessage(new(reply.Content, false, reply.SqlBlocks.Select(sql => new PostgreSqlAssistantSql(sql, context.Database, identity, revision)).ToArray(), modelIdentity: reply.ModelIdentity) { Question = prompt });
            }, taskToken);
            EnsureCurrent();
        }
        catch (OperationCanceledException) { if (!_disposed) AddFailure(prompt, Text.Get("PostgreSqlAssistantCancelled")); }
        catch (Exception error) { if (!_disposed) AddFailure(prompt, Text.Error(error, "AgentRequestFailed")); }
        finally { _pending = null; IsBusy = false; Status = ""; if (_disposed) _conversation.Clear(); Refresh(); }
    }
}
