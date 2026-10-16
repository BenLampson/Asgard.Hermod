using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实设备存储与可控传输，所有模型用例均不访问网络或创建窗口。</summary>
internal sealed class AgentModelTestRuntime : IDisposable
{
    private readonly LocalStorageTestEnvironment _environment = new();
    internal static readonly AgentModelProfile First = new("11111111111111111111111111111111", "Company model", "https://models.example.com/v1", "gpt-4.1", "private-company-key", DateTimeOffset.UnixEpoch);
    internal static readonly AgentModelProfile Second = new("22222222222222222222222222222222", "Personal model", "https://api.example.com/v1", "claude-sonnet-4", "private-personal-key", DateTimeOffset.UnixEpoch);
    internal static readonly AgentModelProfile Third = new("33333333333333333333333333333333", "Local model", "http://localhost:11434/v1", "qwen3:14b", "", DateTimeOffset.UnixEpoch);
    internal AgentModelTestRuntime()
    {
        Services = _environment.CreateServices(); Services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        Store = Services.GetRequiredService<IAgentModelStore>(); Store.Save(new([First, Second, Third], First.Id));
        Catalog = new(Store, Services.GetRequiredService<IAgentPreferenceStore>(), new AgentConfiguration(), Sync,
            Services.GetRequiredService<IConfigurationSyncStateStore>(), Accounts);
        Catalog.Initialize(); Client = new(Catalog);
    }
    internal ServiceProvider Services { get; }
    internal IAgentModelStore Store { get; }
    internal AutomaticSyncUiTestService Sync { get; } = new();
    internal AccountUiTestService Accounts { get; } = new();
    internal AgentModelCatalog Catalog { get; }
    internal ClientTransport Client { get; }
    public void Dispose() { Catalog.Dispose(); Services.Dispose(); _environment.Dispose(); }

    internal sealed class ClientTransport(AgentModelCatalog catalog) : IAgentClient, IAgentSessionClientFactory
    {
        internal List<(AgentModelOptions Options, string Name)> Captured { get; } = [];
        internal Func<CancellationToken, Task<AgentReply>> Handler { get; set; } = _ => Task.FromResult(AgentReply.FromContent("Test response"));
        public AgentModelSession CreateSession() => catalog.CreateSession(this);
        public IAgentClient ForOptions(AgentModelOptions options, string profileName) { Captured.Add((options, profileName)); return new Frozen(this); }
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => throw new InvalidOperationException("A model task must freeze its options first.");
        private sealed class Frozen(ClientTransport owner) : IAgentClient
        {
            public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => owner.Handler(cancellationToken);
        }
    }
}
