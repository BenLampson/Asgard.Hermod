using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>消息是本次执行的安全快照；定位不替换、恢复或执行文稿。</summary>
public sealed partial class PostgreSqlTabViewModel
{
    /// <summary>结构化消息与唯一输出槽绑定。</summary>
    public IReadOnlyList<DatabaseOutputMessage> OutputMessages { get; private set; } = [];
    /// <summary>仅由已经挂载的编辑器消费本地偏移。</summary>
    public Action<int>? LocateSqlRequested { get; set; }
    /// <summary>旧消息可阅读，但编辑后不能再用旧坐标定位。</summary>
    public RelayCommand LocateMessageCommand => field ??= new(item =>
    {
        if (!IsBusy && item is DatabaseOutputMessage message && message.GetOffset(Sql) is { } offset) LocateSqlRequested?.Invoke(offset);
    }, item => !IsBusy && item is DatabaseOutputMessage message && message.GetOffset(Sql) is not null);
    /// <summary>表格复制与导出使用同一安全本地快照，不发送服务器请求。</summary>
    public string MessageCopyText => string.Join("\t", MessageHeaders) + Environment.NewLine + string.Join(Environment.NewLine, MessageValues.Select(row => string.Join("\t", row)));
    private static string[] MessageHeaders => new[] { "DatabaseMessageTime", "DatabaseMessageStatus", "DatabaseMessagePosition", "DatabaseMessageText" }.Select(Text.Get).ToArray();
    private object?[][] MessageValues => OutputMessages.Select(message => new object?[] { message.TimeLabel, message.StatusLabel, message.PositionLabel, message.Message }).ToArray();
    /// <summary>消息导出复用现有文件选择和 CSV 转义，不更改数据结果与编辑权限。</summary>
    public async Task ExportOutputAsync()
    {
        if (!IsMessagesOutput) { await ExportAsync(); return; }
        if (IsBusy || OutputMessages.Count == 0) return;
        var columns = MessageHeaders.Select((name, index) => new PostgreSqlColumn(index, name, "TEXT", typeof(string), false)).ToArray();
        try { await _dialogs.ExportAsync(new PostgreSqlGridViewModel(new(1, 1, columns, MessageValues)), _lifetime.Token); }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlFileFailed"); Refresh(); }
    }
    private void SetOutputMessages(PostgreSqlExecution execution, SqlDocumentAnchor? anchor)
    {
        var rows = new List<DatabaseOutputMessage>(); var time = SelectedTrace?.Started ?? DateTimeOffset.Now;
        foreach (var outcome in execution.Outcomes)
        {
            bool failed = !outcome.Succeeded && execution.Failure is not null;
            rows.Add(new(time, outcome.Succeeded, failed ? Text.Error(execution.Failure!, "PostgreSqlCommandFailed") : Text.Format(outcome.Succeeded ? "PostgreSqlStatementSucceeded" : "PostgreSqlStatementStopped", outcome.Statement, Math.Max(0, outcome.AffectedRows)), failed ? execution.Failure?.Location : null, anchor));
        }
        if (execution.Failure is { } error && rows.Count == 0) rows.Add(new(time, false, Text.Error(error, "PostgreSqlCommandFailed"), error.Location, anchor));
        OutputMessages = rows; NotifyMessages();
    }
    private void SetMessageFailure(string safeMessage)
    {
        OutputMessages = [new(DateTimeOffset.Now, false, safeMessage)]; NotifyMessages();
    }
    private void NotifyMessages()
    {
        foreach (string name in new[] { nameof(OutputMessages), nameof(OutputRequestSummary), nameof(OutputFooterText), nameof(OutputFooterHint) }) OnPropertyChanged(name);
        LocateMessageCommand.NotifyCanExecuteChanged();
    }
    private string MessageFooter => OutputMessages.FirstOrDefault(message => message.GetOffset(Sql) is not null) is { Line: { } line }
        ? Text.Format("DatabaseMessageLocateFooter", line) : Text.Get("DatabaseMessagePreservedFooter");
}
