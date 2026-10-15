using Asgard.Hermod.PostgreSql.UI.Services;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

public sealed partial class PostgreSqlTabViewModel
{
    /// <summary>重连只确认中止与事务结束，保留 SQL 文稿和结果单元格草稿。</summary>
    public async Task<WorkspaceClosePlan?> PrepareReconnectAsync()
    {
        if (CommitInputRequested?.Invoke() == false) return null;
        bool stop = IsBusy;
        if (stop && !await _dialogs.ConfirmStopAsync()) return null;
        bool rollback = _session is not null && _executed && _session.State.InTransaction != false;
        if (rollback && !await _dialogs.ConfirmEndSessionAsync()) return null;
        return new WorkspaceClosePlan(async () =>
        {
            if (stop) { _pending?.Cancel(); if (_completion is not null) await _completion.Task; }
            if (rollback && _session is { IsConnected: true }) await _session.RollbackAsync(_lifetime.Token);
        });
    }
    /// <summary>解除旧物理会话，已有文稿、分页、草稿与未知写入状态原样保留。</summary>
    public async Task DisconnectSessionAsync()
    {
        _assistantContextIdentity = new();
        SetConnectionAvailability(false);
        await StopCompletionAsync();
        var previous = _session; _session = null;
        if (previous is not null) await previous.DisposeAsync();
        _executed = false;
    }
    /// <summary>新连接配置只供下一次显式读取或执行，不自动重放 SQL。</summary>
    public void RebindConnection(InstanceConnectionOptions options) { _options = options; var endpoint = PostgreSqlSessionOptions.Parse(options); Endpoint = Text.Format("PostgreSqlEndpoint", endpoint.Host, endpoint.Port); SetConnectionAvailability(true); }
    /// <summary>光标语句定位尊重 PostgreSQL 美元引号和注释，不改变完整文稿。</summary>
    public async Task ExecuteScopeAsync(string selection, int caret, bool all)
    {
        if (!CanRun || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        string snapshot = Sql;
        await RunAsync(async token =>
        {
            string sql;
            if (all) sql = snapshot;
            else if (selection.Length > 0) sql = selection;
            else
            {
                var session = await SessionAsync(token);
                var statements = await session.SplitAsync(snapshot, token);
                var current = statements.FirstOrDefault(statement => caret >= statement.Start && caret <= statement.Start + statement.Length)
                    ?? (caret >= snapshot.TrimEnd().Length ? statements.LastOrDefault() : null);
                sql = current?.Sql ?? "";
            }
            await ExecuteCoreAsync(sql, token);
        });
    }
    /// <summary>确认草稿和事务后释放旧连接，下一次显式执行连接目标数据库。</summary>
    public async Task ChangeDatabaseAsync(string database)
    {
        if (string.IsNullOrWhiteSpace(database)) { Error = Text.Get("PostgreSqlDatabaseRequired"); Refresh(); return; }
        if (IsBusy || IsManagingScope || !IsQuery || !ConnectionAvailable || Database == database) return;
        if (Scope is not null && !Scope.Contains(database)) { Error = Text.Get("DatabaseSessionTargetOutsideScope"); Refresh(); return; }
        var plan = await PrepareReconnectAsync();
        if (plan is null || _disposed || IsBusy || Scope is not null && !Scope.Contains(database)) return;
        IsManagingScope = true;
        try
        {
            await plan.CommitAsync(); await DisconnectSessionAsync();
            Database = database; Scope?.SelectExecutionTarget(database); RebindConnection(_options);
        }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlCommandFailed"); }
        finally { IsManagingScope = false; Refresh(); }
    }
    /// <summary>每个会话使用该页签的实际数据库，禁止跨库协议伪装。</summary>
    private InstanceConnectionOptions ConnectionForDatabase() => PostgreSqlWorkspaceViewModel.WithDatabase(_options, Database);
    /// <summary>只保存 SQL 文件，失败时保留文稿。</summary>
    public async Task SaveDocumentAsync()
    {
        try
        {
            string snapshot = Sql;
            string? path = await _dialogs.SaveQueryAsync(snapshot, _path, _lifetime.Token);
            if (path is null) return; _path = path; _savedSql = snapshot; DraftChanged();
        }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlFileFailed"); Refresh(); }
    }
    /// <summary>只导出当前加载范围，不隐式重跑查询。</summary>
    public async Task ExportAsync()
    {
        if (SelectedResult is not { } grid || IsBusy) return;
        try { await _dialogs.ExportAsync(grid, _lifetime.Token); }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlFileFailed"); Refresh(); }
    }
    private async Task<bool> ResolveDataDraftAsync()
    {
        if (!HasDataDraft) return true;
        var choice = await _dialogs.ResolveDraftAsync(Header);
        if (choice == PostgreSqlDraftChoice.Cancel) return false;
        if (choice == PostgreSqlDraftChoice.Save) return await SaveChangesAsync();
        foreach (var grid in Results) grid.Revert(); OutcomeUnknown = false; Refresh(); return true;
    }
    /// <summary>确认正在执行的命令、数据草稿、文稿和事务后才关闭。</summary>
    public async Task<bool> RequestCloseAsync()
    {
        var plan = await PrepareCloseAsync();
        if (plan is null) return false;
        try { await plan.CommitAsync(); return true; }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlCommandFailed"); Refresh(); return false; }
    }
    /// <summary>只收集停止、丢弃及回滚决定；用户明确选择保存时仍执行保存。</summary>
    public async Task<WorkspaceClosePlan?> PrepareCloseAsync()
    {
        if (CommitInputRequested?.Invoke() == false) return null;
        bool stop = IsBusy;
        if (stop && !await _dialogs.ConfirmStopAsync()) return null;
        bool discard = false;
        if (HasDataDraft)
        {
            var choice = await _dialogs.ResolveDraftAsync(Header);
            if (choice == PostgreSqlDraftChoice.Cancel) return null;
            if (choice == PostgreSqlDraftChoice.Save && (IsBusy || !await SaveChangesAsync())) return null;
            discard = choice == PostgreSqlDraftChoice.Discard;
        }
        if (IsDocumentDirty)
        {
            var choice = await _dialogs.ResolveDraftAsync(Header, true);
            if (choice == PostgreSqlDraftChoice.Cancel) return null;
            if (choice == PostgreSqlDraftChoice.Save) { await SaveDocumentAsync(); if (IsDocumentDirty) return null; }
        }
        bool rollback = stop || _executed && _session is not null && _session.State.InTransaction != false;
        if (rollback && !await _dialogs.ConfirmEndSessionAsync()) return null;
        return new WorkspaceClosePlan(async () =>
        {
            if (stop) { _pending?.Cancel(); if (_completion is not null) await _completion.Task; }
            if (discard) { foreach (var grid in Results) grid.Revert(); OutcomeUnknown = false; Refresh(); }
            if (rollback && _session is not null && _session.State.InTransaction != false)
                await _session.RollbackAsync(_lifetime.Token);
        });
    }
    /// <summary>取消或等待未完成工作后释放会话资源。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; if (Scope is not null) Scope.PropertyChanged -= OnScopeChanged; _disposed = true; _lifetime.Cancel(); _pending?.Cancel();
        await StopCompletionAsync();
        if (_completion is not null) await _completion.Task;
        foreach (var grid in Results) grid.DraftChanged -= DraftChanged;
        CommitInputRequested = null; InsertSqlRequested = null;
        if (_session is not null) await _session.DisposeAsync(); _lifetime.Dispose();
    }
}
