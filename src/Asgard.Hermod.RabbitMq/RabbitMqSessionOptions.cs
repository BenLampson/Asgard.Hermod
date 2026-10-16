using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.RabbitMq;

/// <summary>冻结连接参数，URI 只包含明确允许的协议、主机、端口和代理路径。</summary>
public sealed record RabbitMqSessionOptions(InstanceConnectionOptions Connection, Uri BaseUri, int TimeoutSeconds)
{
    /// <summary>跨 SSH 仍保留原始 HTTPS 主机名用于 SNI 与证书验证。</summary>
    public bool UsesSsh => Connection.ProviderId == RabbitMqIds.Ssh;
    /// <summary>界面可展示的地址，不包含凭据。</summary>
    public string Address => BaseUri.GetLeftPart(UriPartial.Authority);
    /// <summary>已冻结的协议参数。</summary>
    public string Value(string key, string fallback = "") => Connection.Parameters.GetValueOrDefault(key, fallback);
    /// <summary>转换到共用 SSH 工厂；不另建主机信任策略。</summary>
    public InstanceConnectionOptions SshConnection => new()
    {
        TypeId = SshIds.Type,
        ProviderId = SshIds.Direct,
        Parameters = Connection.Parameters.Where(pair => pair.Key.StartsWith("ssh.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[4..], pair => pair.Value, StringComparer.Ordinal),
    };

    /// <summary>在任何联网之前验证配置，不把不可信路径拼成绝对 URL。</summary>
    public static RabbitMqSessionOptions Parse(InstanceConnectionOptions connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.TypeId != RabbitMqIds.Type || connection.ProviderId is not (RabbitMqIds.Direct or RabbitMqIds.Ssh))
            throw Text.Exception("RabbitMqConfigurationInvalid");
        string Get(string key, string fallback = "") => connection.Parameters.GetValueOrDefault(key, fallback);
        string scheme = Get("scheme", "https"), host = Get("host").Trim();
        if (scheme == "https" && Get("trust", "system") is not ("system" or "custom")) throw Text.Exception("RabbitMqConfigurationInvalid");
        if (scheme is not ("https" or "http") || Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown
            || host.Any(char.IsWhiteSpace) || Get("user").Length is 0 or > 256 || Get("user").Contains(':'))
            throw Text.Exception("RabbitMqConfigurationInvalid");
        if (!int.TryParse(Get("port", scheme == "https" ? "15671" : "15672"), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535 || !int.TryParse(Get("timeout", "15"), NumberStyles.None, CultureInfo.InvariantCulture, out int timeout)
            || timeout is < 1 or > 300)
            throw Text.Exception("RabbitMqConfigurationInvalid");
        string prefix = Get("prefix", "/").Trim();
        if (!prefix.StartsWith('/') || prefix.IndexOfAny(['?', '#', '\\', '%', ':']) >= 0
            || prefix.Split('/').Any(part => part is "." or "..") || prefix.Any(char.IsControl))
            throw Text.Exception("RabbitMqPathInvalid");
        string path = prefix.TrimEnd('/') + "/api/";
        var frozen = new InstanceConnectionOptions
        {
            TypeId = connection.TypeId,
            ProviderId = connection.ProviderId,
            Parameters = new Dictionary<string, string>(connection.Parameters, StringComparer.Ordinal)
        };
        return new(frozen, new UriBuilder(scheme, host, port, path).Uri, timeout);
    }
}
