using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.PostgreSql;

/// <summary>声明公共向导的连接方法、条件字段和安全默认值。</summary>
public static class PostgreSqlProfile
{
    /// <summary>在语言初始化后构造连接器描述。</summary>
    public static InstanceProviderDescriptor Descriptor(string route) => new(route,
        Text.Get(route == PostgreSqlIds.Ssh ? "PostgreSqlSsh" : "PostgreSqlDirect"),
        Text.Get(route == PostgreSqlIds.Ssh ? "PostgreSqlSshHint" : "PostgreSqlDirectHint"),
        route == PostgreSqlIds.Ssh ? "terminal" : "database",
        new(PostgreSqlIds.Type, Text.Get("PostgreSqlType"), Text.Get("PostgreSqlTypeHint"), "database")
        {
            ProviderSelectionTitleResourceKey = "PostgreSqlConnectionMethod",
            ProviderSelectionDescriptionResourceKey = "PostgreSqlConnectionMethodHint",
            ProviderLabelResourceKey = "PostgreSqlConnectionMethod",
            PreserveFieldsOnProviderChange = true,
        }, Fields(route))
    { ConnectionTestDescription = Text.Get("PostgreSqlTestHint") };

    /// <summary>密码和证书信息统一由既有配置存储保护。</summary>
    public static IReadOnlyList<InstanceConfigurationField> Fields(string route)
    {
        var fields = new List<InstanceConfigurationField>
        {
            new("host", Text.Get("PostgreSqlHost"), MaxLength: 253, ValidationRule: InstanceFieldValidationRule.Host),
            new("port", Text.Get("SshPort"), MaxLength: 5, ValidationRule: InstanceFieldValidationRule.Port) { DefaultValue = "5432" },
            new("user", Text.Get("SshUser"), MaxLength: 256),
            new("password", Text.Get("SshPassword"), InstanceFieldKind.Secret, false, 4096) { PreserveWhitespace = true },
            new("database", Text.Get("PostgreSqlDefaultDatabase"), IsRequired: false, MaxLength: 63, Hint: Text.Get("PostgreSqlDefaultDatabaseHint")) { DefaultValue = "postgres" },
            Choice("tls", "PostgreSqlTlsMode", "verify-full", [new("verify-full", Text.Get("PostgreSqlVerifyFull")), new("required", Text.Get("PostgreSqlTlsRequired")), new("disabled", Text.Get("PostgreSqlTlsDisabled"))]),
            Choice("trust", "RedisTrust", "system", [new("system", Text.Get("RedisSystemTrust")), new("custom", Text.Get("RedisCustomTrust"))]) with { Conditions = [new("tls", "verify-full")] },
            new("ca", Text.Get("RedisCa"), InstanceFieldKind.File, MaxLength: 4096, Hint: Text.Get("RedisDeviceFileHint"), IsSensitive: true)
                { Conditions = [new("tls", "verify-full"), new("trust", "custom")] },
            Choice("clientCertificate", "RedisClientAuthentication", "no", [new("no", Text.Get("RedisDisabled")), new("yes", Text.Get("RedisEnabled"))]),
            new("certificate", Text.Get("PostgreSqlClientPfx"), InstanceFieldKind.File, MaxLength: 4096, Hint: Text.Get("RedisDeviceFileHint"), IsSensitive: true)
                { Conditions = [new("clientCertificate", "yes")] },
            new("certificatePassword", Text.Get("RedisCertificatePassword"), InstanceFieldKind.Secret, false, 4096)
                { Conditions = [new("clientCertificate", "yes")], PreserveWhitespace = true },
            new("timeout", Text.Get("SshTimeout"), MaxLength: 3, ValidationRule: InstanceFieldValidationRule.ConnectionTimeout) { DefaultValue = "15" },
            new("commandTimeout", Text.Get("PostgreSqlCommandTimeout"), MaxLength: 3, ValidationRule: InstanceFieldValidationRule.ConnectionTimeout) { DefaultValue = "30" },
        };
        if (route == PostgreSqlIds.Ssh)
            foreach (var definition in SshProfile.Fields(SshIds.Direct).Where(item => item.Key != "timeout"))
                fields.Add(definition with
                {
                    Key = "ssh." + definition.Key,
                    VisibleWhenKey = definition.VisibleWhenKey is null ? null : "ssh." + definition.VisibleWhenKey,
                    Conditions = definition.Conditions.Select(condition => new InstanceFieldCondition("ssh." + condition.Key, condition.Value)).ToArray(),
                });
        return fields;
    }

    private static InstanceConfigurationField Choice(string key, string label, string initial, IReadOnlyList<InstanceFieldChoice> choices) =>
        new(key, Text.Get(label), InstanceFieldKind.Choice) { DefaultValue = initial, Choices = choices };
}
