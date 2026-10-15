namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>共享编辑器负责键盘交互，模块负责 PostgreSQL 安全引用和有界目录候选。</summary>
public sealed partial class PostgreSqlTabViewModel : ICodeCompletionProvider
{
    private readonly SemaphoreSlim _completionGate = new(1, 1);
    private CancellationTokenSource? _completionRequest;
    private readonly Dictionary<string, IReadOnlyList<PostgreSqlObject>> _completionMetadata = new(StringComparer.Ordinal);
    private static readonly string[] Keywords = ["SELECT", "FROM", "WHERE", "JOIN", "LEFT JOIN", "INNER JOIN", "ON", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "RETURNING", "WITH", "INSERT INTO", "UPDATE", "DELETE FROM", "CREATE TABLE", "ALTER TABLE", "EXPLAIN", "DISTINCT", "NULL", "IS NULL", "IS NOT NULL", "AND", "OR", "AS", "CASE", "WHEN", "THEN", "ELSE", "END", "BEGIN", "COMMIT", "ROLLBACK"];

    /// <summary>只读取当前库的模式、关系和用户明确限定的列，失败时保留关键词补全。</summary>
    public async Task<IReadOnlyList<CodeCompletionItem>> CompleteAsync(CodeCompletionRequest request, CancellationToken token)
    {
        if (_disposed || !IsQuery || request.Caret < 0 || request.Caret > request.Text.Length) return [];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _completionGate.WaitAsync(linked.Token);
        _completionRequest = linked;
        try
        {
            int start = request.Caret;
            while (start > 0 && IsIdentifier(request.Text[start - 1])) start--;
            string prefix = request.Text[start..request.Caret].TrimStart('"'), qualifier = "";
            if (start > 0 && request.Text[start - 1] == '.')
            {
                int first = start - 1; while (first > 0 && IsIdentifier(request.Text[first - 1])) first--;
                qualifier = request.Text[first..(start - 1)].Trim('"').Replace("\"\"", "\"", StringComparison.Ordinal);
            }
            if (ConnectionAvailable && !IsBusy)
            {
                try
                {
                    var session = await SessionAsync(linked.Token);
                    await LoadCompletionAsync(session, new(PostgreSqlObjectKind.Database, Database, Database), linked.Token);
                    string schema = Cached().Any(item => item.Kind == PostgreSqlObjectKind.Schema && item.Name == qualifier) ? qualifier : "public";
                    await LoadCompletionAsync(session, new(PostgreSqlObjectKind.Tables, "", Database, Schema: schema), linked.Token);
                    await LoadCompletionAsync(session, new(PostgreSqlObjectKind.Views, "", Database, Schema: schema), linked.Token);
                    foreach (var table in Cached().Where(item => item.Kind is PostgreSqlObjectKind.Table or PostgreSqlObjectKind.View && item.Name == qualifier).Take(4).ToArray())
                        await LoadCompletionAsync(session, new(PostgreSqlObjectKind.Columns, "", Database, table.Name, Schema: table.Schema), linked.Token);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception) { /* 目录读取失败不打断文稿，也不展示原始协议异常。 */ }
            }
            var items = new List<CodeCompletionItem>();
            if (qualifier.Length == 0) items.AddRange(Keywords.Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(value => new CodeCompletionItem(value, value, start, request.Caret - start, Text.Get("PostgreSqlCompletionKeyword"))));
            foreach (var item in Cached().Where(item => item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                bool table = item.Kind is PostgreSqlObjectKind.Table or PostgreSqlObjectKind.View, column = item.Kind == PostgreSqlObjectKind.Column;
                if (!table && !column && item.Kind != PostgreSqlObjectKind.Schema) continue;
                if (qualifier.Length > 0 && !(table && item.Schema == qualifier || column && item.ParentName == qualifier)) continue;
                string insert = PostgreSqlSql.Quote(item.Name);
                if (table && qualifier.Length == 0) insert = PostgreSqlSql.Quote(item.Schema) + "." + insert;
                items.Add(new(item.Name, insert, start, request.Caret - start, Text.Format("PostgreSqlCompletionObject", item.Schema, item.Name)));
            }
            return items.DistinctBy(item => item.InsertText).Take(100).ToArray();
        }
        finally { _completionRequest = null; _completionGate.Release(); }
        IEnumerable<PostgreSqlObject> Cached() => _completionMetadata.Where(pair => pair.Key.StartsWith(Database + "\0", StringComparison.Ordinal)).SelectMany(pair => pair.Value);
        static bool IsIdentifier(char character) => char.IsLetterOrDigit(character) || character is '_' or '$' or '"';
    }

    private async Task LoadCompletionAsync(IPostgreSqlSession session, PostgreSqlObject parent, CancellationToken token)
    {
        string key = Database + "\0" + parent.Schema + "\0" + parent.Kind + "\0" + parent.ParentName;
        if (_completionMetadata.ContainsKey(key) || IsBusy) return;
        var page = await session.ListAsync(parent, 0, token); _completionMetadata[key] = page.Items;
    }

    /// <summary>数据库切换和关闭先停止目录提示，再释放其正在使用的物理会话。</summary>
    private async Task StopCompletionAsync()
    {
        _completionRequest?.Cancel();
        await _completionGate.WaitAsync();
        try { _completionMetadata.Clear(); }
        finally { _completionGate.Release(); }
    }
}
