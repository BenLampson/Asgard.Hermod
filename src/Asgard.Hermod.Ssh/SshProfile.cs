namespace Asgard.Hermod.Ssh;

/// <summary>通过声明式元数据复用公共配置编辑器与持久化校验。</summary>
public static class SshProfile
{
    /// <summary>语言在宿主初始化后构造，因此不缓存翻译后的描述。</summary>
    public static InstanceProviderDescriptor Descriptor(string route) => new(route,
        Text.Get(route switch { SshIds.Jump => "SshJump", SshIds.Proxy => "SshProxy", _ => "SshDirect" }),
        Text.Get(route switch { SshIds.Jump => "SshJumpDescription", SshIds.Proxy => "SshProxyDescription", _ => "SshDirectDescription" }),
        route == SshIds.Direct ? "server" : route == SshIds.Jump ? "route" : "network",
        new(SshIds.Type, Text.Get("SshTypeName"), Text.Get("SshTypeDescription"), "terminal")
        {
            ProviderSelectionTitleResourceKey = "SshConnectionMethod",
            ProviderSelectionDescriptionResourceKey = "SshChooseMethodDescription",
            ProviderLabelResourceKey = "SshConnectionMethod",
            AllowProviderChangeOnEdit = true,
            OmitSaveLocationOnEdit = true,
            DefaultProviderId = SshIds.Direct,
            OmitSaveLocation = true,
        }, Fields(route))
    { ConnectionTestDescription = Text.Get("SshTestDescription") };

    /// <summary>仅声明本路径需要的字段；认证字段通过稳定值控制适用性。</summary>
    public static IReadOnlyList<InstanceConfigurationField> Fields(string route)
    {
        var fields = new List<InstanceConfigurationField>();
        AddHost(fields, "");
        if (route == SshIds.Jump) AddHost(fields, "jump.");
        if (route == SshIds.Proxy)
        {
            fields.Add(new("proxy.type", Text.Get("SshProxyType"), InstanceFieldKind.Choice)
            {
                DefaultValue = "socks5",
                Choices = [new("socks5", "SOCKS5"), new("http", "HTTP CONNECT")],
            });
            fields.Add(new("proxy.host", Text.Get("SshProxyHost"), MaxLength: 253, ValidationRule: InstanceFieldValidationRule.Host));
            fields.Add(new("proxy.port", Text.Get("SshPort"), MaxLength: 5, ValidationRule: InstanceFieldValidationRule.Port) { DefaultValue = "1080" });
            fields.Add(new("proxy.user", Text.Get("SshProxyUser"), IsRequired: false, MaxLength: 255));
            fields.Add(new("proxy.password", Text.Get("SshProxyPassword"), InstanceFieldKind.Secret, false, 255) { PreserveWhitespace = true });
            fields.Add(new("proxy.dns", Text.Get("SshDns"), InstanceFieldKind.Choice)
            {
                DefaultValue = "remote",
                Choices = [new("remote", Text.Get("SshDnsRemote")), new("local", Text.Get("SshDnsLocal"))],
                VisibleWhenKey = "proxy.type",
                VisibleWhenValue = "socks5",
            });
        }
        fields.Add(new("timeout", Text.Get("SshTimeout"), MaxLength: 3, ValidationRule: InstanceFieldValidationRule.ConnectionTimeout) { DefaultValue = "15" });
        return fields;
    }

    /// <summary>跳板和目标采用同一认证集合，但不共享输入值。</summary>
    private static void AddHost(List<InstanceConfigurationField> fields, string prefix)
    {
        fields.Add(new(prefix + "host", Text.Get(prefix.Length == 0 ? "SshHost" : "SshJumpHost"), MaxLength: 253, ValidationRule: InstanceFieldValidationRule.Host));
        fields.Add(new(prefix + "port", Text.Get("SshPort"), MaxLength: 5, ValidationRule: InstanceFieldValidationRule.Port) { DefaultValue = "22" });
        fields.Add(new(prefix + "user", Text.Get("SshUser"), MaxLength: 128));
        fields.Add(new(prefix + "auth", Text.Get("SshAuthentication"), InstanceFieldKind.Choice)
        {
            DefaultValue = SshIds.Password,
            Choices = [new(SshIds.Password, Text.Get("SshPasswordAuthentication")), new(SshIds.PrivateKey, Text.Get("SshKeyAuthentication")), new(SshIds.Agent, Text.Get("SshAgentAuthentication"))],
        });
        fields.Add(new(prefix + "password", Text.Get("SshPassword"), InstanceFieldKind.Secret, MaxLength: 4096)
        { VisibleWhenKey = prefix + "auth", VisibleWhenValue = SshIds.Password, PreserveWhitespace = true });
        fields.Add(new(prefix + "key", Text.Get("SshKeyFile"), InstanceFieldKind.File, MaxLength: 65536, Hint: Text.Get("SshKeyDeviceHint"), IsSensitive: true)
        { VisibleWhenKey = prefix + "auth", VisibleWhenValue = SshIds.PrivateKey });
        fields.Add(new(prefix + "passphrase", Text.Get("SshPassphrase"), InstanceFieldKind.Secret, false, 4096)
        { VisibleWhenKey = prefix + "auth", VisibleWhenValue = SshIds.PrivateKey, PreserveWhitespace = true });
    }
}
