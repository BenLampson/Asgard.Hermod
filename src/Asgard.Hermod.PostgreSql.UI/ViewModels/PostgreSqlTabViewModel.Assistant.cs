using System.Text;
using System.Text.Json;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

public sealed partial class PostgreSqlTabViewModel
{
    /// <summary>视图通过原生编辑器插入文本，保留光标、选区和撤销历史。</summary>
    public Action<string>? InsertSqlRequested { get; set; }
    /// <summary>建议绑定原物理会话，重连和切库会使旧建议的执行资格失效。</summary>
    internal object? AssistantSessionIdentity => _session;

    /// <summary>确认完整 SQL 和实际目标，并在草稿确认后再次验证原会话。</summary>
    public async Task ExecuteAssistantSqlAsync(PostgreSqlAssistantSql suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (!CanRun || _disposed) return;
        bool Matches() => suggestion.Database == Database && ReferenceEquals(suggestion.SessionIdentity, _session);
        if (!Matches()) { Error = Text.Get("PostgreSqlAssistantContextChanged"); Refresh(); return; }
        if (!await _dialogs.ConfirmAssistantSqlAsync(suggestion.Sql, Database) || !Matches() || _disposed || !CanRun) return;
        if (CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        if (!Matches()) { Error = Text.Get("PostgreSqlAssistantContextChanged"); Refresh(); return; }
        await RunAsync(token => Matches() ? ExecuteCoreAsync(suggestion.Sql, token) : Task.CompletedTask);
    }

    /// <summary>用户主动应用生成的 SQL，不触发执行或替换整份文稿。</summary>
    public void InsertAssistantSql(string sql)
    {
        if (_disposed || !IsQuery || IsBusy || string.IsNullOrWhiteSpace(sql)) return;
        if (InsertSqlRequested is { } insert) insert(sql);
        else Sql += (Sql.Length == 0 ? "" : Environment.NewLine) + sql;
    }

    /// <summary>在现有页签会话中串行读取有限元数据；结果只在明确选择后加入上下文。</summary>
    public async Task<AgentSqlContext> ReadAssistantContextAsync(bool includeSql, bool includeResults, CancellationToken cancellationToken)
    {
        if (IsBusy || !ConnectionAvailable || _disposed) throw Text.Exception("PostgreSqlAssistantSessionBusy");
        AgentSqlContext? context = null;
        await RunAsync(async token =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            var session = await SessionAsync(linked.Token);
            var schema = new StringBuilder();
            if (Database.Length > 0)
            {
                var schemas = await session.ListAsync(new(PostgreSqlObjectKind.Database, Database, Database), 0, linked.Token);
                foreach (var scope in schemas.Items.Where(item => item.Kind == PostgreSqlObjectKind.Schema).Take(8))
                    foreach (var kind in new[] { PostgreSqlObjectKind.Tables, PostgreSqlObjectKind.Views })
                    {
                        var page = await session.ListAsync(new(kind, "", Database, Schema: scope.Name), 0, linked.Token);
                        foreach (var item in page.Items.Take(80)) schema.AppendLine(PostgreSqlSql.Quote(item.Schema) + "." + PostgreSqlSql.Quote(item.Name));
                        // 只加载文稿提及的少量表字段，不扫描整库。
                        foreach (var item in page.Items.Where(item => item.Name == Name || Sql.Contains(item.Name, StringComparison.OrdinalIgnoreCase)).Take(4))
                        {
                            var columns = await session.ListAsync(new(PostgreSqlObjectKind.Columns, "", Database, item.Name, Schema: scope.Name), 0, linked.Token);
                            foreach (var column in columns.Items.Take(80))
                                schema.AppendLine(PostgreSqlSql.Quote(scope.Name) + "." + PostgreSqlSql.Quote(item.Name) + "." + PostgreSqlSql.Quote(column.Name) + " " + column.Detail);
                        }
                    }
            }
            string schemaText = schema.ToString();
            context = new(Database, schemaText[..Math.Min(schemaText.Length, 16000)], includeSql ? Sql[..Math.Min(Sql.Length, 16000)] : "",
                includeResults ? AssistantResultSample() : "", Dialect: "PostgreSQL");
        });
        cancellationToken.ThrowIfCancellationRequested();
        return context ?? throw Text.Exception("PostgreSqlAssistantContextFailed");
    }

    private string AssistantResultSample()
    {
        if (SelectedResult is not { } grid) return "";
        var result = grid.Result;
        // 仅分享已加载的基线值，不隐式重跑查询或传递用户尚未提交的单元格草稿。
        var rows = new List<string[]>();
        foreach (var row in result.Rows.Take(20))
        {
            var values = row.Take(16).Select(value =>
            {
                string text = value is byte[]? "[binary]" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
                return text[..Math.Min(text.Length, 256)];
            }).ToArray();
            rows.Add(values);
            if (JsonSerializer.Serialize(rows).Length > 6000) { rows.RemoveAt(rows.Count - 1); break; }
        }
        string Serialize() => JsonSerializer.Serialize(new { columns = result.Columns.Take(16).Select(column => column.Name), rows, sample = true });
        string sample = Serialize();
        while (Encoding.UTF8.GetByteCount(sample) > 8192 && rows.Count > 0) { rows.RemoveAt(rows.Count - 1); sample = Serialize(); }
        return Encoding.UTF8.GetByteCount(sample) <= 8192 ? sample : "";
    }
}
