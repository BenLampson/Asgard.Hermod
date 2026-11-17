namespace Asgard.Hermod.Mcp.Tests;

internal sealed class MemoryMcpStore(params McpTarget[] targets) : IMcpLocalStore
{
    internal string Owner { get; set; } = "";
    internal McpTarget[] Targets { get; set; } = targets;
    internal McpLocalPolicy? Policy { get; set; }
    internal int Opened { get; private set; }

    public McpWorkspace ReadWorkspace() => new(Owner, Targets);
    public string ReadOwner() => Owner;
    public McpLocalPolicy? ReadPolicy() => Policy;
    public void SavePolicy(McpLocalPolicy policy) => Policy = policy;
    public bool IsCurrent(string owner, McpGrant grant) => owner == Owner && Targets.Any(target => McpGrantRules.IsSameTarget(target, grant));
    public InstanceConnectionOptions? OpenTarget(string owner, McpGrant grant)
    {
        if (!IsCurrent(owner, grant)) return null;
        Opened++;
        return new() { TypeId = grant.TypeId, ProviderId = grant.TypeId + ".test", Parameters = new Dictionary<string, string> { ["password"] = "kept-in-hermod" } };
    }
}
