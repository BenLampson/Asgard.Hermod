namespace Asgard.Hermod.Redis;

/// <summary>连接配置只能在协议层解读，秘密不出现在诊断文本中。</summary>
public sealed class RedisSessionOptions
{
    /// <summary>原始配置保留供设备路径和独立认证读取。</summary>
    public required InstanceConnectionOptions Connection { get; init; }
    /// <summary>部署拓扑。</summary>
    public required string Topology { get; init; }
    /// <summary>已验证的公开节点端点。</summary>
    public required IReadOnlyList<RedisEndpoint> Endpoints { get; init; }
    /// <summary>目标数据库，Cluster 固定为零。</summary>
    public int Database { get; init; }
    /// <summary>路径独立于 Redis 认证。</summary>
    public string Route { get; init; } = "direct";
    /// <summary>本次连接超时秒数。</summary>
    public int TimeoutSeconds { get; init; } = 10;
    /// <summary>读取私有参数，不能用于显示或日志。</summary>
    public string Value(string key, string fallback = "") => Connection.Parameters.GetValueOrDefault(key, fallback);
    /// <summary>持久化服务仍负责声明字段校验，此处再次校验协议边界。</summary>
    public static RedisSessionOptions Parse(InstanceConnectionOptions connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.TypeId != RedisIds.Type || connection.ProviderId is not (RedisIds.Standalone or RedisIds.Sentinel or RedisIds.Cluster))
            throw Text.Exception("RedisConfigurationInvalid");
        string Get(string key, string fallback = "") => connection.Parameters.GetValueOrDefault(key, fallback);
        int Number(string key, int fallback, int minimum, int maximum)
        {
            if (!int.TryParse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number < minimum || number > maximum) throw Text.Exception("RedisConfigurationInvalid");
            return number;
        }
        string route = Get("route", "direct");
        if (route is not ("direct" or "proxy" or "ssh") || route == "ssh" && connection.ProviderId != RedisIds.Standalone)
            throw Text.Exception("RedisConfigurationInvalid");
        int database = Number("database", 0, 0, int.MaxValue);
        if (connection.ProviderId == RedisIds.Cluster && database != 0) throw Text.Exception("RedisClusterDatabaseZero");
        var endpoints = connection.ProviderId == RedisIds.Standalone
            ? new[] { RedisEndpoint.Parse(Get("host"), Number("port", 6379, 1, 65535)) }
            : Get("nodes").Split(['\n', '\r', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => RedisEndpoint.Parse(value, connection.ProviderId == RedisIds.Sentinel ? 26379 : 6379)).ToArray();
        if (endpoints.Length is 0 or > 64 || endpoints.Distinct().Count() != endpoints.Length) throw Text.Exception("RedisEndpointInvalid");
        if (connection.ProviderId == RedisIds.Sentinel && string.IsNullOrWhiteSpace(Get("service"))) throw Text.Exception("RedisServiceRequired");
        if (Get("auth", "none") is not ("none" or "password" or "acl") || Get("auth") == "acl" && string.IsNullOrWhiteSpace(Get("user")))
            throw Text.Exception("RedisConfigurationInvalid");
        return new() { Connection = connection, Topology = connection.ProviderId, Endpoints = endpoints, Database = database,
            Route = route, TimeoutSeconds = Number("timeout", 10, 1, 300) };
    }
    /// <summary>凭据、私钥路径及完整连接字符串均不显示。</summary>
    public override string ToString() => "Redis configuration (credentials hidden)";
}
