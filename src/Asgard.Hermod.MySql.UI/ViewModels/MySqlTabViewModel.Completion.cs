namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlTabViewModel : ICodeCompletionProvider
{
    private readonly SemaphoreSlim _metadataCompletionGate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<MySqlObject>> _completionMetadata = new(StringComparer.Ordinal);
    /// <summary>复用查询的物理会话按需读取有界元数据；失败只降级为关键词，不干扰 SQL 编辑。</summary>
    public async Task<IReadOnlyList<CodeCompletionItem>> CompleteAsync(CodeCompletionRequest request, CancellationToken token)
    {
        if (_disposed || !IsQuery) return [];
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _metadataCompletionGate.WaitAsync(linked.Token);
        try
        {
            if (ConnectionAvailable && !IsBusy && !IsManagingScope && (Scope is null || Scope.Contains(Database)))
            {
                try
                {
                    var session = await SessionAsync(linked.Token);
                    if (Database.Length > 0) await LoadAsync(session, new(MySqlObjectKind.Tables, "", Database), 10, linked.Token);
                    if (Database.Length > 0) await LoadAsync(session, new(MySqlObjectKind.Views, "", Database), 10, linked.Token);
                    foreach (var source in MySqlCompletion.Sources(request.Text, Database))
                    {
                        if (IsBusy) break;
                        await LoadAsync(session, new(MySqlObjectKind.Columns, "", source.Database, source.Table), 4, linked.Token);
                    }
                    // 跨库提示只读明确输入的数据库分支，不扫描整个服务器的所有字段。
                    foreach (string db in Databases.Where(db => request.Text.Contains(db + ".", StringComparison.Ordinal) || request.Text.Contains(MySqlSql.Quote(db) + ".", StringComparison.Ordinal)).Take(4))
                    {
                        if (IsBusy) break;
                        await LoadAsync(session, new(MySqlObjectKind.Tables, "", db), 10, linked.Token);
                        await LoadAsync(session, new(MySqlObjectKind.Views, "", db), 10, linked.Token);
                    }
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
                catch (Exception) { /* 目录权限不足时仍可编辑并补全关键词，不泄露 SDK 详情。 */ }
            }
            var metadata = _completionMetadata.Values.SelectMany(items => items).Concat(Databases.Select(db => new MySqlObject(MySqlObjectKind.Database, db, db))).ToArray();
            return MySqlCompletion.Suggest(request.Text, request.Caret, request.SelectionStart, request.SelectionLength, Database, metadata)
                .Select(item => new CodeCompletionItem(item.Name, item.InsertText, item.Start, item.Length, item.Kind is null ? Text.Get("MySqlCompletionKeyword") : item.Detail)).ToArray();
        }
        finally { _metadataCompletionGate.Release(); }
    }
    private async Task LoadAsync(IMySqlSession session, MySqlObject parent, int maximumPages, CancellationToken token)
    {
        if (Scope is not null && !Scope.Contains(parent.Database)) return;
        string key = parent.Kind + "\0" + parent.Database + "\0" + parent.ParentName;
        if (_completionMetadata.ContainsKey(key) || IsBusy) return;
        var items = new List<MySqlObject>();
        for (int pageIndex = 0; pageIndex < maximumPages; pageIndex++)
        {
            token.ThrowIfCancellationRequested();
            var page = await session.ListAsync(parent, pageIndex * 200, token); items.AddRange(page.Items);
            if (!page.HasMore) break;
            if (IsBusy) return;
        }
        _completionMetadata[key] = items;
    }
}
