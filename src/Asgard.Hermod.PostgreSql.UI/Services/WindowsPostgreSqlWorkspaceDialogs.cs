using System.Text;
using Asgard.Hermod.PostgreSql.UI.ViewModels;
using Asgard.Hermod.PostgreSql.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;
using Microsoft.Win32;

namespace Asgard.Hermod.PostgreSql.UI.Services;

/// <summary>公共窗口、原生文件选择和本地 SQL 文件；不自动持久化查询历史。</summary>
public sealed class WindowsPostgreSqlWorkspaceDialogs(Func<Window?> owner) : IPostgreSqlWorkspaceDialogs
{
    /// <summary>提供保存、放弃和取消三种明确选择。</summary>
    public Task<PostgreSqlDraftChoice> ResolveDraftAsync(string name, bool queryDocument = false)
    {
        var dialog = new HermodDialog { Owner = owner(), Title = Text.Get("PostgreSqlUnsavedChanges"), Width = 560, Height = 280 };
        dialog.Content = new TextBlock { Text = Text.Format(queryDocument ? "PostgreSqlQueryCloseHint" : "PostgreSqlGridCloseHint", name), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var result = PostgreSqlDraftChoice.Cancel;
        Add("Cancel", PostgreSqlDraftChoice.Cancel); Add("PostgreSqlDiscard", PostgreSqlDraftChoice.Discard); Add(queryDocument ? "PostgreSqlSaveQuery" : "PostgreSqlSaveChanges", PostgreSqlDraftChoice.Save);
        dialog.FooterContent = new DialogActionBar { Content = buttons }; dialog.Loaded += (_, _) => ((Button)buttons.Children[0]).Focus(); dialog.ShowDialog(); return Task.FromResult(result);
        void Add(string key, PostgreSqlDraftChoice choice)
        {
            var button = new Button { Content = Text.Get(key), Margin = new Thickness(0, 0, 8, 0) };
            if (choice == PostgreSqlDraftChoice.Save) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            button.Click += (_, _) => { result = choice; dialog.DialogResult = choice != PostgreSqlDraftChoice.Cancel; }; buttons.Children.Add(button);
        }
    }
    /// <summary>确认停止执行，明确取消不回滚已执行写入。</summary>
    public Task<bool> ConfirmStopAsync() => Task.FromResult(new ConfirmationDialog(Text.Get("PostgreSqlStopAndClose"), Text.Get("PostgreSqlQueryCancelled"), Text.Get("PostgreSqlStopAndClose")) { Owner = owner() }.ShowDialog() == true);
    /// <summary>确认事务状态和显式回滚。</summary>
    public Task<bool> ConfirmEndSessionAsync() => Task.FromResult(new ConfirmationDialog(Text.Get("PostgreSqlEndTransaction"), Text.Get("PostgreSqlEndTransactionHint"), Text.Get("PostgreSqlRollbackAndContinue")) { Owner = owner() }.ShowDialog() == true);
    /// <summary>建议必须经用户检查完整 SQL，并确认当前目标数据库后才能执行。</summary>
    public Task<bool> ConfirmAssistantSqlAsync(string sql, string database) => Task.FromResult(new ConfirmationDialog(Text.Get("PostgreSqlAssistantExecuteTitle"), Text.Format("PostgreSqlAssistantExecuteHint", database), Text.Get("PostgreSqlAssistantExecute"), preview: sql) { Owner = owner() }.ShowDialog() == true);
    /// <summary>保存完整 SQL 文稿，失败不截断原文件。</summary>
    public async Task<string?> SaveQueryAsync(string sql, string? path, CancellationToken token)
    {
        if (path is null)
        {
            var dialog = new SaveFileDialog { Title = Text.Get("PostgreSqlSaveQuery"), Filter = Text.Get("PostgreSqlSqlFileFilter"), DefaultExt = ".sql", AddExtension = true };
            if (!dialog.ShowDialog(owner()).GetValueOrDefault()) return null; path = dialog.FileName;
        }
        // 先写同目录临时文件，再替换目标，保存失败不截断原文件。
        string temporary = path + ".hermod-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, sql, new UTF8Encoding(false), token); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
    /// <summary>读取有大小边界的本地 SQL 文件。</summary>
    public async Task<string?> OpenQueryAsync(CancellationToken token)
    {
        var dialog = new OpenFileDialog { Title = Text.Get("PostgreSqlOpenQuery"), Filter = Text.Get("PostgreSqlSqlFileFilter") };
        if (!dialog.ShowDialog(owner()).GetValueOrDefault()) return null;
        if (new FileInfo(dialog.FileName).Length > 10 * 1024 * 1024) throw Text.Exception("PostgreSqlFileTooLarge");
        return await File.ReadAllTextAsync(dialog.FileName, token);
    }
    /// <summary>只导出当前加载范围，不隐式重跑查询。</summary>
    public async Task<string?> ExportAsync(PostgreSqlGridViewModel grid, CancellationToken token)
    {
        var dialog = new SaveFileDialog { Title = Text.Get("PostgreSqlExportLoaded"), Filter = Text.Get("PostgreSqlCsvFileFilter"), DefaultExt = ".csv", AddExtension = true };
        if (!dialog.ShowDialog(owner()).GetValueOrDefault()) return null;
        await using var output = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
        await output.WriteLineAsync(string.Join(',', grid.Columns.Select(column => Csv(column.Name))).AsMemory(), token);
        foreach (var row in grid.Rows) await output.WriteLineAsync(string.Join(',', grid.Columns.Select(column => Csv(row.Cells[column.Ordinal].CopyValue))).AsMemory(), token);
        return dialog.FileName;
        static string Csv(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
    /// <summary>并排展示原始、服务器和草稿值，默认保留草稿。</summary>
    public Task ShowConflictAsync(PostgreSqlConflict conflict)
    {
        var dialog = new HermodDialog { Owner = owner(), Title = Text.Get("PostgreSqlConflictTitle"), Width = 900, Height = 600, ResizeMode = ResizeMode.CanResize };
        var panel = new Grid { Margin = new Thickness(24) }; panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new());
        var heading = new TextBlock { Text = Text.Get("PostgreSqlEditConflict"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) }; Grid.SetColumnSpan(heading, 3); panel.Children.Add(heading);
        var expected = conflict.Change.Original.ToDictionary(); foreach (var change in conflict.Change.Changes) expected[change.Key] = change.Value;
        var data = new[] { conflict.Change.Original, conflict.Current, expected };
        for (int i = 0; i < 3; i++)
        {
            panel.ColumnDefinitions.Add(new());
            var field = new FormField { Label = Text.Get(i switch { 0 => "PostgreSqlOriginal", 1 => "PostgreSqlServerValue", _ => "PostgreSqlYourDraft" }), Margin = new Thickness(0, 0, 12, 0) };
            field.Content = new CodeEditor { Text = data[i] is { } values ? string.Join(Environment.NewLine, values.Select(pair => pair.Key + " = " + PostgreSqlValues.Format(pair.Value))) : Text.Get("PostgreSqlRowUnavailable"), IsReadOnly = true };
            Grid.SetRow(field, 1); Grid.SetColumn(field, i); panel.Children.Add(field);
        }
        dialog.Content = panel; dialog.FooterContent = new DialogActionBar { Content = new Button { Content = Text.Get("PostgreSqlKeepDraft"), IsCancel = true } };
        dialog.ShowDialog(); return Task.CompletedTask;
    }
    /// <summary>显示公共独立字段窗口。</summary>
    public Task EditFieldAsync(PostgreSqlFieldEditorViewModel field) { new PostgreSqlFieldDialog(field) { Owner = owner() }.ShowDialog(); return Task.CompletedTask; }
}
