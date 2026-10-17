using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Asgard.Hermod.Sync.Tests;

/// <summary>旧服务端确认缺失必须保留本机配置，数据库迁移保留已有账号内容。</summary>
public sealed class AgentModelCompatibilityTests
{
    [Fact]
    public async Task LegacyServerCannotAcknowledgeModelsAndDoesNotOverwriteLocalState()
    {
        await using var host = new ApiTestHost(); await host.InitializeAsync();
        var states = new MemoryConfigurationSyncStateStore();
        var profile = new AgentModelProfile("11111111111111111111111111111111", "Work", "https://models.example/v1", "model", "private-key", DateTimeOffset.UnixEpoch);
        var initial = new WorkspaceSnapshot(1, [], []) { AgentModels = new([profile], profile.Id) };
        var local = new MemorySyncWorkspace(states) { Value = initial };
        var account = new SyncTestAccount(host.Options.TenantId, host.Options.ApplicationId) { Token = host.Token() };
        using var sync = new ConfigurationSyncService(account, local, states, new LegacyFactory(host),
            new HermodIdentityOptions { Authority = host.Options.Authority, ApplicationId = host.Options.ApplicationId }, new ConfigurationTestRegistry());
        await sync.EnableAsync("model-sync-password", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SynchronizeAsync(TestContext.Current.CancellationToken));
        Assert.Equal("AgentModelSyncUnavailable", error.Data["Hermod.ResourceKey"]);
        Assert.Equal(ConfigurationSyncStatus.Failed, sync.Status); Assert.True(WorkspaceContent.Equal(initial, local.Value));
        Assert.Null(states.Load(states.ActiveOwner).Baseline); Assert.Null(sync.LastSyncedAt);
    }

    [Fact]
    public void ServerVersionTwoMigratesIdempotentlyWithoutChangingExistingConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HermodModelMigrationTests", Guid.NewGuid().ToString("N"));
        string database = Path.Combine(directory, "sync.db");
        try
        {
            var repository = new SqliteCloudWorkspaceRepository(Options.Create(new HermodServerOptions { DatabasePath = database })); repository.Initialize();
            var codec = new ConfigurationCodec(new ConfigurationTestRegistry());
            var encoded = codec.Encode(new(1, [], []), new(1, [], []), new("0", null, [], [], null), "migration-password", "owner");
            var saved = repository.SaveConfiguration("owner", new("sub", "tenant", "app", "user"), new("0", encoded), TestContext.Current.CancellationToken)!;
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
            {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE sync_configuration DROP COLUMN agent_models_json; PRAGMA user_version = 2;"; command.ExecuteNonQuery();
            }
            repository.Initialize(); repository.Initialize();
            var restored = repository.ReadConfiguration("owner", TestContext.Current.CancellationToken);
            Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(restored)); Assert.Null(restored.AgentModels);
            using var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()); check.Open();
            using var query = check.CreateCommand(); query.CommandText = "PRAGMA user_version;"; Assert.Equal(3L, query.ExecuteScalar());
        }
        finally
        {
            string testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HermodModelMigrationTests")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(directory).StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class LegacyFactory(ApiTestHost host) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new LegacyHandler(host.CreateClient())) { BaseAddress = new Uri("http://localhost") };
    }
    private sealed class LegacyHandler(HttpClient upstream) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content is not null)
            {
                var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!;
                (body["configuration"] as JsonObject)?.Remove("agentModels"); clone.Content = JsonContent.Create(body);
            }
            var response = await upstream.SendAsync(clone, cancellationToken);
            var content = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
            (content["data"] as JsonObject)?.Remove("agentModels"); response.Content.Dispose(); response.Content = JsonContent.Create(content); return response;
        }
        protected override void Dispose(bool disposing) { if (disposing) upstream.Dispose(); base.Dispose(disposing); }
    }
}
