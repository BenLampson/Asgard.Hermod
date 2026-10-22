using System.Globalization;

namespace Asgard.Hermod.Ssh;

/// <summary>经共享规则校验的 SSH 参数；目标与跳板身份独立。</summary>
public sealed class SshConnectionOptions
{
    /// <summary>直连、SSH 跳板、菜单型堡垒机或代理的稳定标识。</summary>
    public required string Route { get; init; }
    /// <summary>SSH 认证端点；菜单模式下是堡垒机，选中资产由菜单编号确定。</summary>
    public required SshHostOptions Target { get; init; }
    /// <summary>可选跳板机身份。</summary>
    public SshHostOptions? JumpHost { get; init; }
    /// <summary>堡垒机菜单中的数字选择，不作为 Shell 命令拼接。</summary>
    public string BastionSelection { get; init; } = "";
    /// <summary>发送编号前必须等到此菜单提示符。</summary>
    public string BastionMenuPrompt { get; init; } = "[vbh]>";
    /// <summary>可选目标 Shell 提示符后缀；空值识别常见的美元符或井号。</summary>
    public string BastionShellPrompt { get; init; } = "";
    /// <summary>单次网络连接超时，不包含用户核验指纹的等待。</summary>
    public int TimeoutSeconds { get; init; } = 15;
    /// <summary>代理协议。</summary>
    public string ProxyType { get; init; } = "socks5";
    /// <summary>代理主机。</summary>
    public string ProxyHost { get; init; } = "";
    /// <summary>代理端口。</summary>
    public int ProxyPort { get; init; } = 1080;
    /// <summary>代理可选用户名。</summary>
    public string ProxyUser { get; init; } = "";
    /// <summary>代理可选密码。</summary>
    public string ProxyPassword { get; init; } = "";
    /// <summary>SOCKS5 是否将目标名称交给代理解析。</summary>
    public bool RemoteDns { get; init; } = true;

    /// <summary>忽略未使用认证字段，保留密码原始空格，不接受未注册路由。</summary>
    public static SshConnectionOptions Parse(InstanceConnectionOptions connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.TypeId != SshIds.Type || connection.ProviderId is not (SshIds.Direct or SshIds.Jump or SshIds.Proxy or SshIds.Bastion))
            throw Text.Exception("SshConfigurationInvalid");
        var fields = SshProfile.Fields(connection.ProviderId);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in fields)
            values[definition.Key] = connection.Parameters.GetValueOrDefault(definition.Key) ?? definition.DefaultValue;
        foreach (var definition in fields)
            if (InstanceFieldValidation.IsApplicable(definition, values)
                && InstanceFieldValidation.GetError(definition, values[definition.Key]).Length != 0)
                throw Text.Exception("SshConfigurationInvalid");
        SshHostOptions Host(string prefix) => new()
        {
            Host = values[prefix + "host"].Trim(),
            Port = int.Parse(values[prefix + "port"], CultureInfo.InvariantCulture),
            User = values[prefix + "user"].Trim(),
            Authentication = values[prefix + "auth"],
            Password = values[prefix + "auth"] == SshIds.Password ? values.GetValueOrDefault(prefix + "password", "") : "",
            KeyPath = values[prefix + "auth"] == SshIds.PrivateKey ? values.GetValueOrDefault(prefix + "key", "") : "",
            Passphrase = values[prefix + "auth"] == SshIds.PrivateKey ? values.GetValueOrDefault(prefix + "passphrase", "") : "",
        };
        return new()
        {
            Route = connection.ProviderId,
            Target = Host(""),
            JumpHost = connection.ProviderId == SshIds.Jump ? Host("jump.") : null,
            BastionSelection = values.GetValueOrDefault("bastion.selection", "").Trim(),
            BastionMenuPrompt = values.GetValueOrDefault("bastion.menuPrompt", "[vbh]>").Trim(),
            BastionShellPrompt = values.GetValueOrDefault("bastion.shellPrompt", "").Trim(),
            TimeoutSeconds = int.Parse(values["timeout"], CultureInfo.InvariantCulture),
            ProxyType = values.GetValueOrDefault("proxy.type", "socks5"),
            ProxyHost = values.GetValueOrDefault("proxy.host", ""),
            ProxyPort = int.Parse(values.GetValueOrDefault("proxy.port", "1080"), CultureInfo.InvariantCulture),
            ProxyUser = values.GetValueOrDefault("proxy.user", ""),
            ProxyPassword = values.GetValueOrDefault("proxy.password", ""),
            RemoteDns = values.GetValueOrDefault("proxy.dns", "remote") == "remote",
        };
    }

    /// <summary>避免记录配置时泄漏凭据。</summary>
    public override string ToString() => "SSH connection (credentials hidden)";
}
