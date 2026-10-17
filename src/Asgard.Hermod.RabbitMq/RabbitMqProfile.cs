using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.RabbitMq;

/// <summary>共用向导字段与路由；敏感字段使用既有加密配置存储。</summary>
public static class RabbitMqProfile
{
    /// <summary>在本地化初始化后建立描述符。</summary>
    public static InstanceProviderDescriptor Descriptor(string route) => new(route,
        Text.Get(route == RabbitMqIds.Ssh ? "RabbitMqSsh" : "RabbitMqDirect"),
        Text.Get(route == RabbitMqIds.Ssh ? "RabbitMqSshHint" : "RabbitMqDirectHint"),
        route == RabbitMqIds.Ssh ? "terminal" : "network",
        new(RabbitMqIds.Type, Text.Get("RabbitMqProduct"), Text.Get("RabbitMqTypeHint"), "brand-rabbitmq")
        {
            ProviderSelectionTitleResourceKey = "RabbitMqRoute",
            ProviderSelectionDescriptionResourceKey = "RabbitMqRouteHint",
            ProviderLabelResourceKey = "RabbitMqRoute",
            PreserveFieldsOnProviderChange = true,
            OmitSaveLocation = true,
        }, Fields(route))
    { ConnectionTestDescription = Text.Get("RabbitMqTestHint") };

    /// <summary>Management API 和 SSH 分别定义端点；没有 AMQP 管理回退。</summary>
    public static IReadOnlyList<InstanceConfigurationField> Fields(string route)
    {
        var result = new List<InstanceConfigurationField>
        {
            Choice("scheme", "RabbitMqScheme", "https", [new("https", "HTTPS"), new("http", "HTTP")]),
            new("host", Text.Get("RabbitMqHost"), MaxLength: 253, ValidationRule: InstanceFieldValidationRule.Host),
            new("port", Text.Get("RabbitMqPort"), MaxLength: 5, ValidationRule: InstanceFieldValidationRule.Port) { DefaultValue = "15671" },
            new("user", Text.Get("SshUser"), MaxLength: 256),
            new("password", Text.Get("SshPassword"), InstanceFieldKind.Secret, false, 4096) { PreserveWhitespace = true },
            Choice("trust", "RedisTrust", "system", [new("system", Text.Get("RedisSystemTrust")), new("custom", Text.Get("RedisCustomTrust"))]) with { Conditions = [new("scheme", "https")] },
            new("ca", Text.Get("RedisCa"), InstanceFieldKind.File, MaxLength: 4096, IsSensitive: true, Hint: Text.Get("RedisDeviceFileHint"))
                { Conditions = [new("scheme", "https"), new("trust", "custom")] },
            new("prefix", Text.Get("RabbitMqPrefix"), MaxLength: 2048, Hint: Text.Get("RabbitMqPrefixHint")) { DefaultValue = "/" },
            new("timeout", Text.Get("SshTimeout"), MaxLength: 3, ValidationRule: InstanceFieldValidationRule.ConnectionTimeout) { DefaultValue = "15" },
        };
        if (route == RabbitMqIds.Ssh)
            foreach (var field in SshProfile.Fields(SshIds.Direct).Where(item => item.Key != "timeout"))
                result.Add(field with
                {
                    Key = "ssh." + field.Key,
                    VisibleWhenKey = field.VisibleWhenKey is null ? null : "ssh." + field.VisibleWhenKey,
                    Conditions = field.Conditions.Select(condition => new InstanceFieldCondition("ssh." + condition.Key, condition.Value)).ToArray()
                });
        return result;
    }
    private static InstanceConfigurationField Choice(string key, string label, string initial, IReadOnlyList<InstanceFieldChoice> choices) =>
        new(key, Text.Get(label), InstanceFieldKind.Choice) { DefaultValue = initial, Choices = choices };
}
