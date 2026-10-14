using Asgard.Hermod.MySql.UI.Services;

namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlTabViewModel
{
    /// <summary>光标语句定位使用服务器模式，不改变用户完整文稿。</summary>
    public async Task ExecuteScopeAsync(string selection, int caret, bool all)
    {
        if (IsBusy || !IsQuery) return;
        try
        {
            string sql;
            if (all) sql = Sql;
            else if (selection.Length > 0) sql = selection;
            else
            {
                var session = await SessionAsync(_lifetime.Token);
                var statements = await session.SplitAsync(Sql, _lifetime.Token);
                var current = statements.FirstOrDefault(statement => caret >= statement.Start && caret <= statement.Start + statement.Length)
                    ?? (caret >= Sql.TrimEnd().Length ? statements.LastOrDefault() : null);
                sql = current is null ? "" : current.Delimiter == ";" ? current.Sql : "DELIMITER " + current.Delimiter + Environment.NewLine + current.Sql + current.Delimiter;
            }
            await ExecuteAsync(sql);
        }
        catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); Refresh(); }
    }
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
    public async Task<bool> RequestCloseAsync()
    {
        if (CommitInputRequested?.Invoke() == false) return false;
        if (IsBusy)
        {
            if (!await _dialogs.ConfirmStopAsync()) return false;
            _pending?.Cancel(); if (_completion is not null) await _completion.Task;
        }
        if (!await ResolveDataDraftAsync()) return false;
        if (IsDocumentDirty)
        {
            var choice = await _dialogs.ResolveDraftAsync(Header, true);
            if (choice == MySqlDraftChoice.Cancel) return false;
            if (choice == MySqlDraftChoice.Save) { await SaveDocumentAsync(); if (IsDocumentDirty) return false; }
        }
        if (_executed && _session is not null && _session.State.InTransaction != false)
        {
            if (!await _dialogs.ConfirmEndSessionAsync()) return false;
            try { await _session.RollbackAsync(_lifetime.Token); }
            catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); Refresh(); return false; }
        }
        return true;
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _pending?.Cancel();
        if (_completion is not null) await _completion.Task;
        foreach (var grid in Results) grid.DraftChanged -= DraftChanged;
        CommitInputRequested = null;
        if (_session is not null) await _session.DisposeAsync(); _lifetime.Dispose();
    }
}
