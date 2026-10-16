using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

internal sealed class TestPostgreSql : IPostgreSqlSessionFactory
{
    internal int DatabasePageSize = int.MaxValue;
    internal string[] DatabaseNames = ["app"];
    internal List<string> MetadataDatabases { get; } = [];
    internal static InstanceConnectionOptions Connection => new() { TypeId = PostgreSqlIds.Type, ProviderId = PostgreSqlIds.Direct, Parameters = new Dictionary<string, string> { ["host"] = "localhost", ["user"] = "test", ["database"] = "app" } };
    internal static readonly PostgreSqlColumn[] Columns = [new(0, "id", "INT", typeof(int), false, "app", "users", "id", true, true),
        new(1, "name", "VARCHAR(100)", typeof(string), true, "app", "users", "name", false, true), new(2, "profile", "JSON", typeof(string), true, "app", "users", "profile", false, true),
        new(3, "payload", "BLOB", typeof(byte[]), true, "app", "users", "payload", false, true)];
    internal List<(string Database, string Table, int Offset, int Size, string Filter)> PageReads { get; } = [];
    internal Func<int, int, string, PostgreSqlResult>? PageResult { get; set; }
    internal List<(string Sql, string Database)> ExecutedSql { get; } = [];
    internal List<(string Sql, string Database)> ReadOnlyQueries { get; } = [];
    internal Exception? ReadOnlyError { get; set; }
    internal Exception? ExplainError { get; set; }
    internal List<(string Database, string Schema, string Table)> SchemaReads { get; } = [];
    internal List<string> ExplainedSql { get; } = [];
    internal PostgreSqlTable Table { get; } = new("app", "users", Columns, "r");
    internal Dictionary<string, object?> Values { get; } = new() { ["id"] = 1, ["name"] = "Alice", ["profile"] = "{\"ok\":true}", ["payload"] = new byte[] { 0, 255 } };
    internal int Writes, Executions, Opens, Disposals;
    internal int Rollbacks;
    internal bool InTransaction;
    internal bool Unknown, CommitBeforeUnknown;
    internal TaskCompletionSource? BlockedQuery;
    internal bool IgnoreQueryCancellation;
    internal Exception? ListFailure { get; set; }
    internal PostgreSqlResult? ResultOverride { get; set; }
    internal TaskCompletionSource? BlockedOpen;
    internal Exception? OpenError { get; set; }
    internal List<Session> Sessions { get; } = [];
    internal List<InstanceConnectionOptions> OpenedOptions { get; } = [];
    public async Task<IPostgreSqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    { Opens++; OpenedOptions.Add(options); if (BlockedOpen is { } blocked) await blocked.Task; if (OpenError is { } error) throw error; var session = new Session(this, options); Sessions.Add(session); return session; }
    internal PostgreSqlResult Result() => ResultOverride ?? new(1, 1, Columns, [Columns.Select(column => PostgreSqlValues.Clone(Values[column.Name])).ToArray()], EditableTable: Table);
    internal sealed class Session(TestPostgreSql server, InstanceConnectionOptions options) : IPostgreSqlSession
    {
        public PostgreSqlSessionOptions Options => PostgreSqlSessionOptions.Parse(options);
        public bool IsConnected { get; private set; } = true;
        public event EventHandler? ConnectionLost;
        internal void Disconnect() { IsConnected = false; ConnectionLost?.Invoke(this, EventArgs.Empty); }
        public string CurrentDatabase => Options.Database;
        public PostgreSqlSessionState State => new(true, server.InTransaction);
        public Task<IReadOnlyList<PostgreSqlStatement>> SplitAsync(string sql, CancellationToken token = default) => Task.FromResult(PostgreSqlSql.Split(sql));
        public Task<string> ReadDefinitionAsync(PostgreSqlObject item, CancellationToken token = default) => Task.FromResult("CREATE TABLE users(id INT PRIMARY KEY, name VARCHAR(100));");
        public Task<PostgreSqlObjectPage> ListAsync(PostgreSqlObject parent, int offset, CancellationToken token = default)
        {
            server.MetadataDatabases.Add(parent.Database);
            if (server.ListFailure is { } error) throw error;
            PostgreSqlObject[] items = parent.Kind switch
            {
                PostgreSqlObjectKind.Root => server.DatabaseNames.Select(name => new PostgreSqlObject(PostgreSqlObjectKind.Database, name, name)).ToArray(),
                PostgreSqlObjectKind.Database => [new(PostgreSqlObjectKind.Schema, "public", CurrentDatabase, Schema: "public"), new(PostgreSqlObjectKind.Schema, "sales", CurrentDatabase, Schema: "sales")],
                PostgreSqlObjectKind.Schema => [new(PostgreSqlObjectKind.Tables, "", CurrentDatabase, Schema: parent.Schema), new(PostgreSqlObjectKind.Views, "", CurrentDatabase, Schema: parent.Schema)],
                PostgreSqlObjectKind.Tables => [new(PostgreSqlObjectKind.Table, "users", CurrentDatabase, Schema: parent.Schema)],
                PostgreSqlObjectKind.Table => [new(PostgreSqlObjectKind.Columns, "", "app", "users"), new(PostgreSqlObjectKind.Indexes, "", "app", "users"), new(PostgreSqlObjectKind.ForeignKeys, "", "app", "users"), new(PostgreSqlObjectKind.Triggers, "", "app", "users")],
                PostgreSqlObjectKind.Columns => Columns.Select(column => new PostgreSqlObject(PostgreSqlObjectKind.Column, column.Name, CurrentDatabase, "users", column.TypeName, parent.Schema)).ToArray(),
                _ => [],
            };
            if (parent.Kind == PostgreSqlObjectKind.Root) return Task.FromResult(new PostgreSqlObjectPage(items.Skip(offset).Take(server.DatabasePageSize).ToArray(), (long)offset + server.DatabasePageSize < items.Length));
            return Task.FromResult(new PostgreSqlObjectPage(items, false));
        }
        public Task<PostgreSqlTable> DescribeAsync(string database, string table, CancellationToken token = default) => Task.FromResult(server.Table);
        public Task<PostgreSqlTable> DescribeAsync(string database, string schema, string table, CancellationToken token = default) => Task.FromResult(server.Table with { Database = database, Schema = schema });
        public Task<PostgreSqlExecution> ExplainAsync(string sql, string database, CancellationToken token = default)
        { server.ExplainedSql.Add(sql); if (server.ExplainError is { } error) throw error; return Task.FromResult(new PostgreSqlExecution([server.Result()], [new(1, 0, true)], null, TimeSpan.FromMilliseconds(1), State)); }
        public async Task<PostgreSqlExecution> ExecuteAsync(string sql, string database, CancellationToken token = default)
        {
            server.ExecutedSql.Add((sql, database)); server.Executions++; if (server.BlockedQuery is { } blocked) { if (server.IgnoreQueryCancellation) await blocked.Task; else await blocked.Task.WaitAsync(token); }
            return new([server.Result()], [new(1, 0, true)], null, TimeSpan.FromMilliseconds(2), State);
        }
        public async Task<PostgreSqlExecution> ExecuteReadOnlyAsync(string sql, string database, CancellationToken token = default)
        {
            Asgard.Hermod.Abstractions.Models.ReadOnlySqlQuery.Validate(sql);
            server.ReadOnlyQueries.Add((sql, database));
            if (server.ReadOnlyError is { } error) throw error;
            return await ExecuteAsync(sql, database, token);
        }
        public Task<PostgreSqlResult> ReadTableAsync(string database, string table, int offset, CancellationToken token = default) => ReadTableAsync(database, table, offset, 200, "", token);
        public Task<PostgreSqlResult> ReadTableAsync(string database, string schema, string table, int offset, CancellationToken token = default) => ReadTableAsync(database, schema, table, offset, 200, "", token);
        public Task<PostgreSqlResult> ReadTableAsync(string database, string schema, string table, int offset, int pageSize, string filter, CancellationToken token = default)
        { server.SchemaReads.Add((database, schema, table)); return ReadTableAsync(database, table, offset, pageSize, filter, token); }
        public Task<PostgreSqlResult> ReadTableAsync(string database, string table, int offset, int pageSize, string filter, CancellationToken token = default)
        { server.PageReads.Add((database, table, offset, pageSize, filter)); return Task.FromResult(server.PageResult?.Invoke(offset, pageSize, filter) ?? server.Result()); }
        public Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(PostgreSqlTable table, IReadOnlyDictionary<string, object?> key, CancellationToken token = default) => Task.FromResult<IReadOnlyDictionary<string, object?>>(server.Values.ToDictionary(pair => pair.Key, pair => PostgreSqlValues.Clone(pair.Value)));
        public Task SaveAsync(PostgreSqlTable table, IReadOnlyList<PostgreSqlRowChange> changes, CancellationToken token = default)
        {
            server.Writes++;
            foreach (var change in changes)
            {
                if (change.Original.Any(pair => !PostgreSqlValues.Equal(pair.Value, server.Values[pair.Key]))) throw new PostgreSqlConflict(change, server.Values.ToDictionary());
                if (!server.Unknown || server.CommitBeforeUnknown) foreach (var pair in change.Changes) server.Values[pair.Key] = PostgreSqlValues.Clone(pair.Value);
            }
            if (server.Unknown) throw new PostgreSqlFailure("PostgreSqlSaveOutcomeUnknown", "POSTGRESQL_WRITE_UNKNOWN");
            return Task.CompletedTask;
        }
        public Task<PostgreSqlSessionState> ReadStateAsync(CancellationToken token = default) => Task.FromResult(State);
        public Task RollbackAsync(CancellationToken token = default) { server.Rollbacks++; server.InTransaction = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { if (IsConnected) server.Disposals++; IsConnected = false; return ValueTask.CompletedTask; }
    }
}

internal sealed class TestPostgreSqlDialogs : IPostgreSqlWorkspaceDialogs
{
    internal bool ActionAllowed = true;
    internal int RemovalConfirmations, ActionConfirmations;
    internal Asgard.Hermod.UI.Models.DatabaseRemovalDecision? RemovalDecision;
    internal IReadOnlyList<DatabaseIdentity>? SelectedDatabases;
    internal Func<Task<Asgard.Hermod.UI.Models.DatabaseRemovalDecision?>>? RemovalConfirmation { get; set; }
    public Task<bool> ConfirmSessionActionAsync(string instanceName, string sessionName, string actionKey, string endpoint = "") { ActionConfirmations++; return Task.FromResult(ActionAllowed); }
    public Task<IReadOnlyList<DatabaseIdentity>?> SelectSessionDatabasesAsync(string instanceName, string endpoint, string sessionName, IReadOnlyList<DatabaseIdentity> candidates, IReadOnlyList<DatabaseIdentity> included) => Task.FromResult(SelectedDatabases);
    public Task<Asgard.Hermod.UI.Models.DatabaseRemovalDecision?> ConfirmDatabaseRemovalAsync(string instanceName, string endpoint, Asgard.Hermod.UI.Models.DatabaseRemovalRequest request)
    { RemovalConfirmations++; return RemovalConfirmation?.Invoke() ?? Task.FromResult(RemovalDecision); }
    internal PostgreSqlDraftChoice Choice = PostgreSqlDraftChoice.Cancel;
    internal int Conflicts, Fields;
    internal Func<PostgreSqlFieldEditorViewModel, Task>? FieldOpened { get; set; }
    internal Func<Task<bool>>? AssistantConfirmation { get; set; }
    internal int AssistantConfirmations;
    public Task<PostgreSqlDraftChoice> ResolveDraftAsync(string name, bool queryDocument = false) => Task.FromResult(Choice);
    public Task<bool> ConfirmStopAsync() => Task.FromResult(true);
    public Task<bool> ConfirmEndSessionAsync() => Task.FromResult(true);
    public Task<bool> ConfirmAssistantSqlAsync(string sql, string database) { AssistantConfirmations++; return AssistantConfirmation?.Invoke() ?? Task.FromResult(false); }
    public Task<string?> SaveQueryAsync(string sql, string? path, CancellationToken token) => Task.FromResult<string?>("query.sql");
    public Task<string?> OpenQueryAsync(CancellationToken token) => Task.FromResult<string?>(null);
    public Task<string?> ExportAsync(PostgreSqlGridViewModel grid, CancellationToken token) => Task.FromResult<string?>(null);
    public Task ShowConflictAsync(PostgreSqlConflict conflict) { Conflicts++; return Task.CompletedTask; }
    public Task EditFieldAsync(PostgreSqlFieldEditorViewModel field) { Fields++; return FieldOpened?.Invoke(field) ?? Task.CompletedTask; }
}
