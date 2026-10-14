using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

internal sealed class TestMySql : IMySqlSessionFactory
{
    internal static InstanceConnectionOptions Connection => new() { TypeId = MySqlIds.Type, ProviderId = MySqlIds.Direct, Parameters = new Dictionary<string, string> { ["host"] = "localhost", ["user"] = "test", ["database"] = "app" } };
    internal static readonly MySqlColumn[] Columns = [new(0, "id", "INT", typeof(int), false, "app", "users", "id", true, true),
        new(1, "name", "VARCHAR(100)", typeof(string), true, "app", "users", "name", false, true), new(2, "profile", "JSON", typeof(string), true, "app", "users", "profile", false, true),
        new(3, "payload", "BLOB", typeof(byte[]), true, "app", "users", "payload", false, true)];
    internal MySqlTable Table { get; } = new("app", "users", Columns, "InnoDB");
    internal Dictionary<string, object?> Values { get; } = new() { ["id"] = 1, ["name"] = "Alice", ["profile"] = "{\"ok\":true}", ["payload"] = new byte[] { 0, 255 } };
    internal int Writes, Executions, Opens, Disposals;
    internal bool Unknown, CommitBeforeUnknown;
    internal TaskCompletionSource? BlockedQuery;
    internal Exception? ListFailure;
    internal MySqlResult? ResultOverride;
    internal TaskCompletionSource? BlockedOpen;
    internal Exception? OpenError;
    public async Task<IMySqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    { Opens++; if (BlockedOpen is { } blocked) await blocked.Task; if (OpenError is { } error) throw error; return new Session(this); }
    internal MySqlResult Result() => ResultOverride ?? new(1, 1, Columns, [Columns.Select(column => MySqlValues.Clone(Values[column.Name])).ToArray()], EditableTable: Table);
    private sealed class Session(TestMySql server) : IMySqlSession
    {
        public MySqlSessionOptions Options => MySqlSessionOptions.Parse(Connection);
        public bool IsConnected { get; private set; } = true;
        public string CurrentDatabase => "app";
        public MySqlSessionState State => new(true, false);
        public Task<IReadOnlyList<MySqlStatement>> SplitAsync(string sql, CancellationToken token = default) => Task.FromResult(MySqlSql.Split(sql));
        public Task<string> ReadDefinitionAsync(MySqlObject item, CancellationToken token = default) => Task.FromResult("CREATE TABLE users(id INT PRIMARY KEY, name VARCHAR(100));");
        public Task<MySqlObjectPage> ListAsync(MySqlObject parent, int offset, CancellationToken token = default)
        {
            if (server.ListFailure is { } error) throw error;
            MySqlObject[] items = parent.Kind switch
            {
                MySqlObjectKind.Root => [new(MySqlObjectKind.Database, "app", "app")],
                MySqlObjectKind.Database => [new(MySqlObjectKind.Tables, "", "app"), new(MySqlObjectKind.Views, "", "app"), new(MySqlObjectKind.Procedures, "", "app"), new(MySqlObjectKind.Functions, "", "app"), new(MySqlObjectKind.Events, "", "app")],
                MySqlObjectKind.Tables => [new(MySqlObjectKind.Table, "users", "app")],
                MySqlObjectKind.Table => [new(MySqlObjectKind.Columns, "", "app", "users"), new(MySqlObjectKind.Indexes, "", "app", "users"), new(MySqlObjectKind.ForeignKeys, "", "app", "users"), new(MySqlObjectKind.Triggers, "", "app", "users")],
                MySqlObjectKind.Columns => Columns.Select(column => new MySqlObject(MySqlObjectKind.Column, column.Name, "app", "users", column.TypeName)).ToArray(),
                _ => [],
            };
            return Task.FromResult(new MySqlObjectPage(items, false));
        }
        public Task<MySqlTable> DescribeAsync(string database, string table, CancellationToken token = default) => Task.FromResult(server.Table);
        public async Task<MySqlExecution> ExecuteAsync(string sql, string database, CancellationToken token = default)
        {
            server.Executions++; if (server.BlockedQuery is { } blocked) await blocked.Task.WaitAsync(token);
            return new([server.Result()], [new(1, 0, true)], null, TimeSpan.FromMilliseconds(2), State);
        }
        public Task<MySqlResult> ReadTableAsync(string database, string table, int offset, CancellationToken token = default) => Task.FromResult(server.Result());
        public Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(MySqlTable table, IReadOnlyDictionary<string, object?> key, CancellationToken token = default) => Task.FromResult<IReadOnlyDictionary<string, object?>>(server.Values.ToDictionary(pair => pair.Key, pair => MySqlValues.Clone(pair.Value)));
        public Task SaveAsync(MySqlTable table, IReadOnlyList<MySqlRowChange> changes, CancellationToken token = default)
        {
            server.Writes++;
            foreach (var change in changes)
            {
                if (change.Original.Any(pair => !MySqlValues.Equal(pair.Value, server.Values[pair.Key]))) throw new MySqlConflict(change, server.Values.ToDictionary());
                if (!server.Unknown || server.CommitBeforeUnknown) foreach (var pair in change.Changes) server.Values[pair.Key] = MySqlValues.Clone(pair.Value);
            }
            if (server.Unknown) throw new MySqlFailure("MySqlSaveOutcomeUnknown", "MYSQL_WRITE_UNKNOWN");
            return Task.CompletedTask;
        }
        public Task<MySqlSessionState> ReadStateAsync(CancellationToken token = default) => Task.FromResult(State);
        public Task RollbackAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { if (IsConnected) server.Disposals++; IsConnected = false; return ValueTask.CompletedTask; }
    }
}

internal sealed class TestMySqlDialogs : IMySqlWorkspaceDialogs
{
    internal MySqlDraftChoice Choice = MySqlDraftChoice.Cancel;
    internal int Conflicts, Fields;
    internal Func<MySqlFieldEditorViewModel, Task>? FieldOpened;
    public Task<MySqlDraftChoice> ResolveDraftAsync(string name, bool queryDocument = false) => Task.FromResult(Choice);
    public Task<bool> ConfirmStopAsync() => Task.FromResult(true);
    public Task<bool> ConfirmEndSessionAsync() => Task.FromResult(true);
    public Task<string?> SaveQueryAsync(string sql, string? path, CancellationToken token) => Task.FromResult<string?>("query.sql");
    public Task<string?> OpenQueryAsync(CancellationToken token) => Task.FromResult<string?>(null);
    public Task<string?> ExportAsync(MySqlGridViewModel grid, CancellationToken token) => Task.FromResult<string?>(null);
    public Task ShowConflictAsync(MySqlConflict conflict) { Conflicts++; return Task.CompletedTask; }
    public Task EditFieldAsync(MySqlFieldEditorViewModel field) { Fields++; return FieldOpened?.Invoke(field) ?? Task.CompletedTask; }
}
