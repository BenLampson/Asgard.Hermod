namespace Asgard.Hermod.PostgreSql;

/// <summary>再次校验协议边界，禁止多主机列表和无限超时。</summary>
public sealed class PostgreSqlSessionOptions
{
    /// <summary>仅向协议层提供的私有连接配置。</summary>
    public required InstanceConnectionOptions Connection { get; init; }
    /// <summary>数据库的逻辑主机名，也是 TLS 身份校验目标。</summary>
    public required string Host { get; init; }
    /// <summary>目标 TCP 端口。</summary>
    public int Port { get; init; }
    /// <summary>数据库认证用户，与 SSH 用户独立。</summary>
    public required string User { get; init; }
    /// <summary>此页签或连接的数据库上下文，不由树选择隐式修改。</summary>
    public string Database { get; init; } = "postgres";
    /// <summary>使用稳定标识声明的 TLS 策略。</summary>
    public string TlsMode { get; init; } = "verify-full";
    /// <summary>建立连接的有界超时秒数。</summary>
    public int ConnectTimeout { get; init; } = 15;
    /// <summary>执行命令的有界超时秒数。</summary>
    public int CommandTimeout { get; init; } = 30;
    /// <summary>是否通过公共 SSH 转发契约建立路由。</summary>
    public bool UsesSsh => Connection.ProviderId == PostgreSqlIds.Ssh;
    /// <summary>私有配置仅供协议使用，不用于日志。</summary>
    public string Value(string key, string fallback = "") => Connection.Parameters.GetValueOrDefault(key, fallback);

    /// <summary>校验并解析协议选项或字段值，不接受不安全的隐式降级。</summary>
    public static PostgreSqlSessionOptions Parse(InstanceConnectionOptions connection)
    {
        if (connection.TypeId != PostgreSqlIds.Type || connection.ProviderId is not (PostgreSqlIds.Direct or PostgreSqlIds.Ssh))
            throw Text.Exception("PostgreSqlConfigurationInvalid");
        string Get(string key, string fallback = "") => connection.Parameters.GetValueOrDefault(key, fallback);
        int Number(string key, string initial, int maximum)
        {
            if (!int.TryParse(Get(key, initial), NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > maximum)
                throw Text.Exception("PostgreSqlConfigurationInvalid");
            return number;
        }
        string host = Get("host"), user = Get("user"), database = Get("database", "postgres"), tls = Get("tls", "verify-full");
        if (InstanceFieldValidation.GetError(new("host", "", ValidationRule: InstanceFieldValidationRule.Host), host).Length != 0
            || string.IsNullOrWhiteSpace(user) || user.Length > 256 || database.Length > 64 || user.Any(char.IsControl) || database.Any(char.IsControl)
            || tls is not ("verify-full" or "required" or "disabled") || Get("trust", "system") is not ("system" or "custom")
            || Get("clientCertificate", "no") is not ("yes" or "no")
            || Get("clientCertificate") == "yes" && (tls == "disabled" || Get("certificate").Length == 0)
            || tls == "verify-full" && Get("trust") == "custom" && Get("ca").Length == 0)
            throw Text.Exception("PostgreSqlConfigurationInvalid");
        return new()
        {
            Connection = connection,
            Host = host,
            User = user,
            Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
            Port = Number("port", "5432", 65535),
            TlsMode = tls,
            ConnectTimeout = Number("timeout", "15", 300),
            CommandTimeout = Number("commandTimeout", "30", 300)
        };
    }

    /// <summary>提供模块的公开状态或生命周期入口，协议行为使用稳定标识。</summary>
    public override string ToString() => "PostgreSQL configuration (credentials hidden)";
}
