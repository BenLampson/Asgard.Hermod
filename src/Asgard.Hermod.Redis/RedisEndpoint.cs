namespace Asgard.Hermod.Redis;

/// <summary>解析独立的主机与端口，拒绝含凭据或 URL 的连接字符串。</summary>
public sealed record RedisEndpoint(string Host, int Port)
{
    /// <summary>支持域名、IPv4 与方括号 IPv6，协议数字保持 invariant。</summary>
    public static RedisEndpoint Parse(string value, int defaultPort = 6379)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Contains('@') || value.Contains('/') || value.Any(char.IsWhiteSpace))
            throw Text.Exception("RedisEndpointInvalid");
        string host; string? port = null;
        if (value.StartsWith('['))
        {
            int end = value.IndexOf(']');
            if (end < 0) throw Text.Exception("RedisEndpointInvalid");
            host = value[1..end];
            if (end + 1 < value.Length)
            {
                if (value[end + 1] != ':') throw Text.Exception("RedisEndpointInvalid");
                port = value[(end + 2)..];
            }
        }
        else
        {
            int index = value.LastIndexOf(':');
            if (index >= 0 && value.IndexOf(':') == index) { host = value[..index]; port = value[(index + 1)..]; }
            else host = value;
        }
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown) throw Text.Exception("RedisEndpointInvalid");
        int number = defaultPort;
        if (port is not null && (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number is < 1 or > 65535))
            throw Text.Exception("RedisEndpointInvalid");
        return new(host, number);
    }
    /// <summary>只显示公开端点，不包含认证信息。</summary>
    public override string ToString() => (Host.Contains(':') ? "[" + Host + "]" : Host) + ":" + Port.ToString(CultureInfo.InvariantCulture);
    /// <summary>交给 SDK 的端点。</summary>
    public EndPoint ToEndPoint() => IPAddress.TryParse(Host, out var ip) ? new IPEndPoint(ip, Port) : new DnsEndPoint(Host, Port);
}
