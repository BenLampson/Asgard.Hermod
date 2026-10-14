namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlTabViewModel
{
    /// <summary>只有完整基线和已验证列才进入编辑；其他单元格仍可查看。</summary>
    public async Task<bool> PrepareCellAsync(MySqlGridViewModel grid, MySqlRowViewModel row, MySqlCellViewModel cell)
    {
        if (IsBusy || OutcomeUnknown || !grid.IsEditable || !cell.Column.CanUpdate || cell.Column.Generated) return false;
        if (Results.Any(other => !ReferenceEquals(other, grid) && other.IsDirty)) { Error = Text.Get("MySqlOtherResultDraft"); Refresh(); return false; }
        if (row.Baseline is not null) return true;
        bool prepared = false;
        await RunAsync(async token =>
        {
            var table = grid.Result.EditableTable!;
            var projection = row.OriginalProjection;
            if (table.Columns.Count == projection.Count && table.Columns.All(column => projection.ContainsKey(column.Name)))
                row.Baseline = projection.ToDictionary(pair => pair.Key, pair => MySqlValues.Clone(pair.Value));
            else
            {
                var session = await SessionAsync(token);
                var current = await session.ReadRowAsync(table, projection, token);
                if (projection.Any(pair => !current.TryGetValue(pair.Key, out var value) || !MySqlValues.Equal(pair.Value, value)))
                    throw new MySqlFailure("MySqlEditConflict", "MYSQL_CONFLICT");
                row.Baseline = current;
            }
            prepared = true;
        });
        return prepared;
    }

    /// <summary>打开当前行的单个字段窗口，不编辑整列。</summary>
    public async Task OpenFieldAsync(MySqlGridViewModel grid, MySqlRowViewModel row, MySqlCellViewModel cell)
    {
        bool editable = await PrepareCellAsync(grid, row, cell);
        string key = grid.Result.EditableTable is { } table ? string.Join(", ", table.Keys.Select(column => column.Name + "=" + MySqlValues.Format(row.OriginalProjection.GetValueOrDefault(column.Name)))) : Text.Format("MySqlResultRow", Array.IndexOf(grid.Rows.ToArray(), row) + 1);
        using var field = new MySqlFieldEditorViewModel(cell, Text.Format("MySqlFieldContext", cell.Column.Database, cell.Column.Table, cell.Column.Name, key), editable, Instance);
        await _dialogs.EditFieldAsync(field);
    }

    private async Task<bool> SaveChangesAsync()
    {
        if (IsBusy || OutcomeUnknown || CommitInputRequested?.Invoke() == false || !HasDataDraft) return false;
        bool saved = false;
        await RunAsync(async token =>
        {
            var dirty = Results.Where(grid => grid.IsDirty).ToArray();
            if (dirty.Length != 1 || dirty[0].Result.EditableTable is not { } table) throw Text.Exception("MySqlReadOnlyResult");
            var rows = dirty[0].Rows.Where(row => row.IsDirty).ToArray();
            // 用户 SQL 会话及事务保持独立，表格提交使用专用新会话。
            await using var writer = await _factory.OpenAsync(_options, token);
            try { await writer.SaveAsync(table, rows.Select(row => row.Change).ToArray(), token); }
            catch (MySqlConflict conflict) { await _dialogs.ShowConflictAsync(conflict); throw; }
            catch (MySqlFailure failure) when (failure.Code == "MYSQL_WRITE_UNKNOWN") { OutcomeUnknown = true; throw; }
            try
            {
                var confirmed = new List<(MySqlRowViewModel Row, IReadOnlyDictionary<string, object?> Values)>();
                foreach (var row in rows)
                {
                    var key = row.Change.Original.ToDictionary(); foreach (var pair in row.Change.Changes) key[pair.Key] = pair.Value;
                    confirmed.Add((row, await writer.ReadRowAsync(table, key, token)));
                }
                foreach (var (row, values) in confirmed) row.Accept(values);
            }
            catch (Exception) { OutcomeUnknown = true; throw Text.Exception("MySqlSaveConfirmedReloadFailed"); }
            Message = Text.Get("MySqlChangesSaved"); saved = true;
        });
        return saved;
    }

    private async Task VerifyAsync()
    {
        if (!OutcomeUnknown) return;
        await RunAsync(async token =>
        {
            await using var verifier = await _factory.OpenAsync(_options, token);
            var confirmations = new List<(MySqlRowViewModel Row, IReadOnlyDictionary<string, object?> Values)>();
            bool allOriginal = true, allExpected = true;
            foreach (var grid in Results.Where(grid => grid.IsDirty))
            {
                var table = grid.Result.EditableTable!;
                foreach (var row in grid.Rows.Where(row => row.IsDirty))
                {
                    var expected = row.Change.Original.ToDictionary(); foreach (var pair in row.Change.Changes) expected[pair.Key] = pair.Value;
                    IReadOnlyDictionary<string, object?> current;
                    try { current = await verifier.ReadRowAsync(table, row.Change.Original, token); }
                    catch (MySqlFailure failure) when (failure.Code == "MYSQL_ROW_MISSING") { current = await verifier.ReadRowAsync(table, expected, token); }
                    allOriginal &= row.Change.Original.All(pair => MySqlValues.Equal(pair.Value, current.GetValueOrDefault(pair.Key)));
                    allExpected &= table.Columns.Where(column => !column.Generated).All(column => MySqlValues.Equal(expected[column.Name], current.GetValueOrDefault(column.Name)));
                    confirmations.Add((row, current));
                }
            }
            if (allExpected) { foreach (var (row, values) in confirmations) row.Accept(values); OutcomeUnknown = false; Message = Text.Get("MySqlVerifySaved"); }
            else if (allOriginal) { OutcomeUnknown = false; Message = Text.Get("MySqlVerifyUnchanged"); }
            else throw Text.Exception("MySqlVerifyConflict");
        });
    }
}
