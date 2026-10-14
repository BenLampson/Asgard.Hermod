using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.MySql;

/// <summary>声明公共向导的连接方法、条件字段和安全默认值。</summary>
public static class MySqlProfile
{
    /// <summary>在语言初始化后构造连接器描述。</summary>
    public static InstanceProviderDescriptor Descriptor(string route) => new(route,
        Text.Get(route == MySqlIds.Ssh ? "MySqlSsh" : "MySqlDirect"),
        Text.Get(route == MySqlIds.Ssh ? "MySqlSshHint" : "MySqlDirectHint"),
        route == MySqlIds.Ssh ? "terminal" : "database",
        new(MySqlIds.Type, "MySQL", Text.Get("MySqlTypeHint"), "database")
        {
            ProviderSelectionTitleResourceKey = "MySqlConnectionMethod",
            ProviderSelectionDescriptionResourceKey = "MySqlConnectionMethodHint",
            ProviderLabelResourceKey = "MySqlConnectionMethod",
            AllowProviderChangeOnEdit = true,
        }, Fields(route))
    { ConnectionTestDescription = Text.Get("MySqlTestHint") };

    /// <summary>密码和证书信息统一由既有配置存储保护。</summary>
    public static IReadOnlyList<InstanceConfigurationField> Fields(string route)
    {
        var fields = new List<InstanceConfigurationField>
        {
            new("host", Text.Get("MySqlHost"), MaxLength: 253, ValidationRule: InstanceFieldValidationRule.Host),
            new("port", Text.Get("SshPort"), MaxLength: 5, ValidationRule: InstanceFieldValidationRule.Port) { DefaultValue = "3306" },
            new("user", Text.Get("SshUser"), MaxLength: 256),
            new("password", Text.Get("SshPassword"), InstanceFieldKind.Secret, false, 4096) { PreserveWhitespace = true },
            new("database", Text.Get("MySqlDefaultDatabase"), IsRequired: false, MaxLength: 64, Hint: Text.Get("MySqlDefaultDatabaseHint")),
            Choice("tls", "MySqlTlsMode", "verify-full", [new("verify-full", Text.Get("MySqlVerifyFull")), new("required", Text.Get("MySqlTlsRequired")), new("disabled", Text.Get("MySqlTlsDisabled"))]),
            Choice("trust", "RedisTrust", "system", [new("system", Text.Get("RedisSystemTrust")), new("custom", Text.Get("RedisCustomTrust"))]) with { Conditions = [new("tls", "verify-full")] },
            new("ca", Text.Get("RedisCa"), InstanceFieldKind.File, MaxLength: 4096, Hint: Text.Get("RedisDeviceFileHint"), IsSensitive: true)
                { Conditions = [new("tls", "verify-full"), new("trust", "custom")] },
            Choice("clientCertificate", "RedisClientAuthentication", "no", [new("no", Text.Get("RedisDisabled")), new("yes", Text.Get("RedisEnabled"))]),
            new("certificate", Text.Get("MySqlClientPfx"), InstanceFieldKind.File, MaxLength: 4096, Hint: Text.Get("RedisDeviceFileHint"), IsSensitive: true)
                { Conditions = [new("clientCertificate", "yes")] },
            new("certificatePassword", Text.Get("RedisCertificatePassword"), InstanceFieldKind.Secret, false, 4096)
                { Conditions = [new("clientCertificate", "yes")], PreserveWhitespace = true },
            new("timeout", Text.Get("SshTimeout"), MaxLength: 3, ValidationRule: InstanceFieldValidationRule.ConnectionTimeout) { DefaultValue = "15" },
            new("commandTimeout", Text.Get("MySqlCommandTimeout"), MaxLength: 3, ValidationRule: InstanceFieldValidationRule.ConnectionTimeout) { DefaultValue = "30" },
        };
        if (route == MySqlIds.Ssh)
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
