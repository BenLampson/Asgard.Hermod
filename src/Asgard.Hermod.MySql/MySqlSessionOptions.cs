namespace Asgard.Hermod.MySql;

/// <summary>再次校验协议边界，禁止多主机列表和无限超时。</summary>
public sealed class MySqlSessionOptions
{
    public required InstanceConnectionOptions Connection { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; }
    public required string User { get; init; }
    public string Database { get; init; } = "";
    public string TlsMode { get; init; } = "verify-full";
    public int ConnectTimeout { get; init; } = 15;
    public int CommandTimeout { get; init; } = 30;
    public bool UsesSsh => Connection.ProviderId == MySqlIds.Ssh;
    /// <summary>私有配置仅供协议使用，不用于日志。</summary>
    public string Value(string key, string fallback = "") => Connection.Parameters.GetValueOrDefault(key, fallback);

    public static MySqlSessionOptions Parse(InstanceConnectionOptions connection)
    {
        if (connection.TypeId != MySqlIds.Type || connection.ProviderId is not (MySqlIds.Direct or MySqlIds.Ssh))
            throw Text.Exception("MySqlConfigurationInvalid");
        string Get(string key, string fallback = "") => connection.Parameters.GetValueOrDefault(key, fallback);
        int Number(string key, string initial, int maximum)
        {
            if (!int.TryParse(Get(key, initial), NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > maximum)
                throw Text.Exception("MySqlConfigurationInvalid");
            return number;
        }
        string host = Get("host"), user = Get("user"), database = Get("database"), tls = Get("tls", "verify-full");
        if (InstanceFieldValidation.GetError(new("host", "", ValidationRule: InstanceFieldValidationRule.Host), host).Length != 0
            || string.IsNullOrWhiteSpace(user) || user.Length > 256 || database.Length > 64 || user.Any(char.IsControl) || database.Any(char.IsControl)
            || tls is not ("verify-full" or "required" or "disabled") || Get("trust", "system") is not ("system" or "custom")
            || Get("clientCertificate", "no") is not ("yes" or "no")
            || Get("clientCertificate") == "yes" && (tls == "disabled" || Get("certificate").Length == 0)
            || tls == "verify-full" && Get("trust") == "custom" && Get("ca").Length == 0)
            throw Text.Exception("MySqlConfigurationInvalid");
        return new()
        {
            Connection = connection,
            Host = host,
            User = user,
            Database = database,
            Port = Number("port", "3306", 65535),
            TlsMode = tls,
            ConnectTimeout = Number("timeout", "15", 300),
            CommandTimeout = Number("commandTimeout", "30", 300)
        };
    }

    public override string ToString() => "MySQL configuration (credentials hidden)";
}
