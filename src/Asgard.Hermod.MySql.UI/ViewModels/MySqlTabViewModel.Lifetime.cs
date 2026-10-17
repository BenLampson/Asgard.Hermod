using Asgard.Hermod.MySql.UI.Services;

namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlTabViewModel
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
        await _sessionOpenGate.WaitAsync();
        try { var previous = _session; _session = null; if (previous is not null) await previous.DisposeAsync(); }
        finally { _sessionOpenGate.Release(); }
        _completionMetadata.Clear();
        _executed = false;
    }
    /// <summary>新连接配置只供下一次显式读取或执行，不自动重放 SQL。</summary>
    public void RebindConnection(InstanceConnectionOptions options) { _options = options; var endpoint = MySqlSessionOptions.Parse(options); Endpoint = Text.Format("MySqlEndpoint", endpoint.Host, endpoint.Port); SetConnectionAvailability(true); }
    /// <summary>光标语句定位使用服务器模式，不改变用户完整文稿。</summary>
    public async Task ExecuteScopeAsync(string selection, int caret, bool all, int selectionStart = -1)
    {
        if (!CanRun || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        string snapshot = Sql;
        await RunAsync(async token =>
        {
            string sql; Asgard.Hermod.UI.Models.SqlDocumentAnchor? anchor = null;
            if (all) anchor = new(snapshot, 0);
            else if (selectionStart >= 0 && selectionStart <= snapshot.Length - selection.Length && snapshot.AsSpan(selectionStart, selection.Length).SequenceEqual(selection)) anchor = new(snapshot, selectionStart);
            if (all) sql = snapshot;
            else if (selection.Length > 0) sql = selection;
            else
            {
                var session = await SessionAsync(token);
                var statements = await session.SplitAsync(snapshot, token);
                var current = statements.FirstOrDefault(statement => caret >= statement.Start && caret <= statement.Start + statement.Length)
                    ?? (caret >= snapshot.TrimEnd().Length ? statements.LastOrDefault() : null);
                sql = current is null ? "" : current.Delimiter == ";" ? current.Sql : "DELIMITER " + current.Delimiter + Environment.NewLine + current.Sql + current.Delimiter;
                if (current is not null) anchor = new(snapshot, current.Start + snapshot.AsSpan(current.Start, current.Length).Length - snapshot.AsSpan(current.Start, current.Length).TrimStart().Length, current.Delimiter == ";" ? 0 : ("DELIMITER " + current.Delimiter + Environment.NewLine).Length);
            }
            await ExecuteCoreAsync(sql, token, anchor: anchor);
        });
    }
    /// <summary>确认草稿和事务后变更此查询的数据库上下文。</summary>
    public async Task ChangeDatabaseAsync(string database)
    {
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
        catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); }
        finally { IsManagingScope = false; Refresh(); }
    }
    /// <summary>只保存 SQL 文件，失败时保留文稿。</summary>
    public async Task SaveDocumentAsync()
    {
        try
        {
            string snapshot = Sql;
            string? path = await _dialogs.SaveQueryAsync(snapshot, _path, _lifetime.Token);
            if (path is null) return; _path = path; _savedSql = snapshot; DraftChanged();
        }
        catch (Exception error) { Error = Text.Error(error, "MySqlFileFailed"); Refresh(); }
    }
    /// <summary>只导出当前加载范围，不隐式重跑查询。</summary>
    public async Task ExportAsync()
    {
        if (SelectedResult is not { } grid || IsBusy) return;
        try { await _dialogs.ExportAsync(grid, _lifetime.Token); }
        catch (Exception error) { Error = Text.Error(error, "MySqlFileFailed"); Refresh(); }
    }
    private async Task<bool> ResolveDataDraftAsync()
    {
        if (!HasDataDraft) return true;
        var choice = await _dialogs.ResolveDraftAsync(Header);
        if (choice == MySqlDraftChoice.Cancel) return false;
        if (choice == MySqlDraftChoice.Save) return await SaveChangesAsync();
        foreach (var grid in Results) grid.Revert(); OutcomeUnknown = false; Refresh(); return true;
    }
    /// <summary>确认正在执行的命令、数据草稿、文稿和事务后才关闭。</summary>
    public async Task<bool> RequestCloseAsync()
    {
        var plan = await PrepareCloseAsync();
        if (plan is null) return false;
        try { await plan.CommitAsync(); return true; }
        catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); Refresh(); return false; }
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
            if (choice == MySqlDraftChoice.Cancel) return null;
            if (choice == MySqlDraftChoice.Save && (IsBusy || !await SaveChangesAsync())) return null;
            discard = choice == MySqlDraftChoice.Discard;
        }
        if (IsDocumentDirty)
        {
            var choice = await _dialogs.ResolveDraftAsync(Header, true);
            if (choice == MySqlDraftChoice.Cancel) return null;
            if (choice == MySqlDraftChoice.Save) { await SaveDocumentAsync(); if (IsDocumentDirty) return null; }
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
        if (_completion is not null) await _completion.Task;
        foreach (var grid in Results) grid.DraftChanged -= DraftChanged;
        CommitInputRequested = null;
        await _sessionOpenGate.WaitAsync();
        try { if (_session is not null) await _session.DisposeAsync(); }
        finally { _sessionOpenGate.Release(); }
        InsertSqlRequested = null; ExecutionTraces.Clear(); SelectedTrace = null; _completionMetadata.Clear(); _lifetime.Dispose();
    }
}
