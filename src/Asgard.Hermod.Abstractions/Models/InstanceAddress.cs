using System.Globalization;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>显式白名单投影安全地址，不让连接串、账号、查询令牌或任意参数进入导航快照。</summary>
public static class InstanceAddress
{
    /// <summary>只识别既有协议地址字段；未知类型与无效地址不猜测、不显示原值。</summary>
    public static string Format(string typeId, IReadOnlyDictionary<string, string> parameters, string providerId = "")
    {
        if (typeId == "storage")
        {
            string endpoint = parameters.GetValueOrDefault("endpoint") ?? "";
            if (!endpoint.Contains("://", StringComparison.Ordinal)) endpoint = "https://" + endpoint;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || string.IsNullOrEmpty(uri.Host)) return "";
            return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        }
        if (typeId == "redis" && parameters.TryGetValue("nodes", out var nodes) && nodes.Length > 0)
        {
            var addresses = new List<string>();
            foreach (string node in nodes.Split(['\r', '\n', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (node.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '@' or '/' or '\\' or '?' or '#' or ';' or '=')) return "";
                if (!Uri.TryCreate("redis://" + node, UriKind.Absolute, out var uri) || Uri.CheckHostName(uri.Host.Trim('[', ']')) == UriHostNameType.Unknown) return "";
                int nodePort = uri.Port == -1 ? providerId == "redis.sentinel" ? 26379 : 6379 : uri.Port;
                if (nodePort is <= 0 or > 65535) return "";
                addresses.Add(uri.Host + ":" + nodePort.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(", ", addresses);
        }
        if (typeId is not ("mysql" or "redis" or "ssh")) return "";
        string host = parameters.GetValueOrDefault("host")?.Trim() ?? "";
        if (host.Length == 0 || host.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)
            || character is '@' or '/' or '\\' or '?' or '#' or ';' or '=')) return "";
        if (Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown) return "";
        if (host.Contains(':') && !host.StartsWith('[')) host = "[" + host + "]";
        string portText = parameters.GetValueOrDefault("port") ?? (typeId == "mysql" ? "3306" : typeId == "redis" ? "6379" : "22");
        return int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535
            ? host + ":" + port.ToString(CultureInfo.InvariantCulture) : "";
    }
}
