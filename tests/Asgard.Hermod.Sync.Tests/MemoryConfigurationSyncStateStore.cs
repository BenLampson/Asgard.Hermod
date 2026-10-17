namespace Asgard.Hermod.Sync.Tests;

/// <summary>按所有者隔离的内存状态；DPAPI 由真实 SQLite 测试覆盖。</summary>
internal sealed class MemoryConfigurationSyncStateStore : IConfigurationSyncStateStore
{
    private readonly Dictionary<string, ConfigurationSyncState> _states = [];
    public string ActiveOwner { get; set; } = "";
    public ConfigurationSyncState Load(string owner) => _states.GetValueOrDefault(owner) ?? new();
    public void Save(string owner, ConfigurationSyncState state) => _states[owner] = state;
}
