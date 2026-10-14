using Asgard.Hermod.MySql.UI.Services;

namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlTabViewModel
{
    /// <summary>光标语句定位使用服务器模式，不改变用户完整文稿。</summary>
    public async Task ExecuteScopeAsync(string selection, int caret, bool all)
    {
        if (IsBusy || !IsQuery || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
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
                sql = current is null ? "" : current.Delimiter == ";" ? current.Sql : "DELIMITER " + current.Delimiter + Environment.NewLine + current.Sql + current.Delimiter;
            }
            await ExecuteCoreAsync(sql, token);
        });
    }
    /// <summary>确认草稿和事务后变更此查询的数据库上下文。</summary>
    public async Task ChangeDatabaseAsync(string database)
    {
        if (IsBusy || !IsQuery || Database == database || !await ResolveDataDraftAsync()) return;
        if (_session is not null && _executed && _session.State.InTransaction != false)
        {
            if (!await _dialogs.ConfirmEndSessionAsync()) return;
            try { await _session.RollbackAsync(_lifetime.Token); } catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); Refresh(); return; }
        }
        Database = database; Refresh();
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
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _pending?.Cancel();
        if (_completion is not null) await _completion.Task;
        foreach (var grid in Results) grid.DraftChanged -= DraftChanged;
        CommitInputRequested = null;
        if (_session is not null) await _session.DisposeAsync(); _lifetime.Dispose();
    }
}
