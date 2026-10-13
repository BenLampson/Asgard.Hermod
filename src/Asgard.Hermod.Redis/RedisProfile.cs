using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.Redis;

/// <summary>拓扑与路由通过稳定标识声明，复用公共向导、秘密编辑器和持久化。</summary>
public static class RedisProfile
{
    /// <summary>在宿主应用语言后创建翻译描述。</summary>
    public static InstanceProviderDescriptor Descriptor(string topology) => new(topology,
        Text.Get(topology switch { RedisIds.Sentinel => "RedisSentinel", RedisIds.Cluster => "RedisCluster", _ => "RedisStandalone" }),
        Text.Get(topology switch { RedisIds.Sentinel => "RedisSentinelHint", RedisIds.Cluster => "RedisClusterHint", _ => "RedisStandaloneHint" }),
        "database", new(RedisIds.Type, "Redis", Text.Get("RedisTypeHint"), "database")
        {
            ProviderSelectionTitleResourceKey = "RedisTopology", ProviderSelectionDescriptionResourceKey = "RedisTopologyHint",
            ProviderLabelResourceKey = "RedisTopology",
        }, Fields(topology)) { ConnectionTestDescription = Text.Get("RedisTestHint") };

    /// <summary>Sentinel 与数据节点使用独立认证及 TLS 配置。</summary>
    public static IReadOnlyList<InstanceConfigurationField> Fields(string topology)
    {
        var fields = new List<InstanceConfigurationField>();
        if (topology == RedisIds.Standalone)
        {
            fields.Add(new("host", Text.Get("RedisHost"), MaxLength: 253, ValidationRule: InstanceFieldValidationRule.Host));
            fields.Add(new("port", Text.Get("SshPort"), MaxLength: 5, ValidationRule: InstanceFieldValidationRule.Port) { DefaultValue = "6379" });
        }
        else fields.Add(new("nodes", Text.Get("RedisNodes"), MaxLength: 8192, Hint: Text.Get("RedisNodesHint")));
        if (topology == RedisIds.Sentinel) fields.Add(new("service", Text.Get("RedisService"), MaxLength: 256));
        fields.Add(new("database", Text.Get("RedisDatabase"), MaxLength: 9, Hint: Text.Get(topology == RedisIds.Cluster ? "RedisClusterDatabaseHint" : "RedisDatabaseHint")) { DefaultValue = "0" });
        AddAuthentication(fields, ""); AddTls(fields, "");
        if (topology == RedisIds.Sentinel) { AddAuthentication(fields, "sentinel."); AddTls(fields, "sentinel."); }
        fields.Add(Choice("route", "RedisRoute", "direct", topology == RedisIds.Standalone
            ? [new("direct", Text.Get("SshDirect")), new("proxy", Text.Get("SshProxy")), new("ssh", Text.Get("RedisSshTunnel"))]
            : [new("direct", Text.Get("SshDirect")), new("proxy", Text.Get("SshProxy"))]));
        foreach (var definition in SshProfile.Fields(SshIds.Proxy).Where(item => item.Key.StartsWith("proxy.", StringComparison.Ordinal)))
            fields.Add(definition with { Conditions = [new("route", "proxy")] });
        if (topology == RedisIds.Standalone)
            foreach (var definition in SshProfile.Fields(SshIds.Direct).Where(item => item.Key != "timeout"))
                fields.Add(definition with { Key = "ssh." + definition.Key,
                    VisibleWhenKey = definition.VisibleWhenKey is null ? null : "ssh." + definition.VisibleWhenKey,
                    Conditions = [new("route", "ssh")] });
        fields.Add(new("timeout", Text.Get("SshTimeout"), MaxLength: 3, ValidationRule: InstanceFieldValidationRule.ConnectionTimeout) { DefaultValue = "10" });
        return fields;
    }

    private static InstanceConfigurationField Choice(string key, string label, string initial, IReadOnlyList<InstanceFieldChoice> choices) =>
        new(key, Text.Get(label), InstanceFieldKind.Choice) { DefaultValue = initial, Choices = choices };

    private static void AddAuthentication(List<InstanceConfigurationField> fields, string prefix)
    {
        fields.Add(Choice(prefix + "auth", prefix.Length == 0 ? "RedisAuthentication" : "RedisSentinelAuthentication", "none",
            [new("none", Text.Get("RedisNoAuthentication")), new("password", Text.Get("SshPasswordAuthentication")), new("acl", "ACL")]));
        fields.Add(new(prefix + "password", Text.Get("SshPassword"), InstanceFieldKind.Secret, MaxLength: 4096)
        { VisibleWhenKey = prefix + "auth", VisibleWhenValue = "password", PreserveWhitespace = true });
        fields.Add(new(prefix + "user", Text.Get("SshUser"), MaxLength: 256) { VisibleWhenKey = prefix + "auth", VisibleWhenValue = "acl" });
        fields.Add(new(prefix + "acl.password", Text.Get("SshPassword"), InstanceFieldKind.Secret, MaxLength: 4096)
        { VisibleWhenKey = prefix + "auth", VisibleWhenValue = "acl", PreserveWhitespace = true });
    }

    private static void AddTls(List<InstanceConfigurationField> fields, string prefix)
    {
        fields.Add(Choice(prefix + "tls", prefix.Length == 0 ? "RedisTls" : "RedisSentinelTls", "no",
            [new("no", Text.Get("RedisDisabled")), new("yes", Text.Get("RedisEnabled"))]));
        fields.Add(Choice(prefix + "trust", "RedisTrust", "system", [new("system", Text.Get("RedisSystemTrust")), new("custom", Text.Get("RedisCustomTrust"))])
            with { Conditions = [new(prefix + "tls", "yes")] });
        fields.Add(new(prefix + "ca", Text.Get("RedisCa"), InstanceFieldKind.File, MaxLength: 4096, Hint: Text.Get("RedisDeviceFileHint"), IsSensitive: true)
        { Conditions = [new(prefix + "tls", "yes"), new(prefix + "trust", "custom")] });
        fields.Add(new(prefix + "tls.host", Text.Get("RedisTlsHost"), IsRequired: false, MaxLength: 253, ValidationRule: InstanceFieldValidationRule.Host)
        { Conditions = [new(prefix + "tls", "yes")] });
        fields.Add(Choice(prefix + "mtls", "RedisClientAuthentication", "no", [new("no", Text.Get("RedisDisabled")), new("yes", Text.Get("RedisEnabled"))])
            with { Conditions = [new(prefix + "tls", "yes")] });
        foreach (string name in new[] { "certificate", "privateKey", "certificatePassword" })
            fields.Add(new(prefix + name, Text.Get("Redis" + char.ToUpperInvariant(name[0]) + name[1..]),
                name == "certificatePassword" ? InstanceFieldKind.Secret : InstanceFieldKind.File, name != "certificatePassword", 4096,
                name == "certificatePassword" ? "" : Text.Get("RedisDeviceFileHint"), true)
            { Conditions = [new(prefix + "tls", "yes"), new(prefix + "mtls", "yes")], PreserveWhitespace = name == "certificatePassword" });
    }
}
