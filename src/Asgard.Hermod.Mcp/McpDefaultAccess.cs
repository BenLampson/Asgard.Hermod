using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Mcp;

/// <summary>默认允许模式以会话代号定位实例，并在每次调用时冻结具体资源。</summary>
internal static class McpDefaultAccess
{
    internal static string Id(McpSessionAccess session, McpTarget target, string resource = "", string qualifier = "")
    {
        byte[] input = Encoding.UTF8.GetBytes(string.Join('\n', session.Policy.Secret, target.Id, target.TypeId, target.Revision, resource, qualifier));
        return Convert.ToHexString(SHA256.HashData(input).AsSpan(0, 16));
    }

    internal static McpGrant InstanceGrant(McpSessionAccess session, McpTarget target) => new(
        Id(session, target), target.Id, target.TypeId, target.Revision, "", "", "", true,
        AllowUpload: target.TypeId == "storage", AllowDownload: target.TypeId == "storage",
        AllowCreateFolder: target.TypeId == "storage", AllowDelete: target.TypeId == "storage",
        AllowSignedLinks: target.TypeId == "storage", AllowOverwrite: target.TypeId == "storage",
        AllowUnrestrictedUpload: target.TypeId == "storage");

    internal static McpGrant ForRequest(McpSessionAccess session, McpTarget target, McpArguments args, string tool)
    {
        var grant = InstanceGrant(session, target);
        if (tool == McpToolCatalog.ListResources) return grant;
        string resource = target.TypeId switch
        {
            "storage" => args.String("bucket"),
            "mysql" or "postgresql" or "redis" => args.String("database", target.TypeId == "redis" ? "0" : ""),
            "rabbitmq" => args.String("vhost"),
            "ssh" => args.String("service"),
            _ => "",
        };
        string qualifier = target.TypeId == "postgresql" ? args.String("schema", "public") : "";
        bool valid = target.TypeId switch
        {
            "storage" => resource.Length > 0 && char.IsAsciiLetterOrDigit(resource[0]) && char.IsAsciiLetterOrDigit(resource[^1])
                && resource.All(value => char.IsAsciiLetterOrDigit(value) || value is '.' or '-'),
            "mysql" => !string.IsNullOrWhiteSpace(resource),
            "postgresql" => !string.IsNullOrWhiteSpace(resource) && !string.IsNullOrWhiteSpace(qualifier)
                && Encoding.UTF8.GetByteCount(resource) <= 63 && Encoding.UTF8.GetByteCount(qualifier) <= 63,
            "redis" => int.TryParse(resource, NumberStyles.None, CultureInfo.InvariantCulture, out int db) && db >= 0,
            "rabbitmq" => resource.Length > 0 && resource is not ("." or ".."),
            "ssh" => resource.Length == 0 || McpGrantRules.IsServiceName(resource),
            _ => false,
        };
        if (!valid) throw Text.Exception("McpSelectResourceRequired");
        return grant with { Id = Id(session, target, resource, qualifier), Resource = resource, Qualifier = qualifier };
    }

    internal static McpApprovalScope Scope(McpTarget target) => new(target.Id, target.TypeId, target.Revision);
}
