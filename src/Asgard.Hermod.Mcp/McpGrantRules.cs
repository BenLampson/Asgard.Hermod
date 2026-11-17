using System.Globalization;
using System.Text;

namespace Asgard.Hermod.Mcp;

/// <summary>授权只保存字面资源名，不接受通配授权、命令或查询表达式。</summary>
internal static class McpGrantRules
{
    internal const int MaximumGrants = 128;

    internal static bool Supports(string type) => type is "storage" or "mysql" or "postgresql" or "ssh" or "redis" or "rabbitmq";

    internal static bool IsSameTarget(McpTarget target, McpGrant grant) => target.Id == grant.InstanceId
        && target.TypeId == grant.TypeId && target.Revision == grant.Revision;

    internal static void Validate(McpGrant grant)
    {
        if (!Valid(grant.Resource, 253) || !Valid(grant.Qualifier, 1024) || !Valid(grant.Table, 128))
            throw Text.Exception("McpInvalidGrant");

        bool valid = grant.TypeId switch
        {
            "storage" => grant.Resource.Length > 0 && char.IsAsciiLetterOrDigit(grant.Resource[0])
                && char.IsAsciiLetterOrDigit(grant.Resource[^1]) && grant.Resource.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-')
                && grant.Table.Length == 0 && IsObjectPath(grant.Qualifier),
            "mysql" => !string.IsNullOrWhiteSpace(grant.Resource) && grant.Qualifier.Length == 0 && (!grant.ShareContent || !string.IsNullOrWhiteSpace(grant.Table)),
            "postgresql" => !string.IsNullOrWhiteSpace(grant.Resource) && !string.IsNullOrWhiteSpace(grant.Qualifier)
                && (!grant.ShareContent || !string.IsNullOrWhiteSpace(grant.Table))
                && new[] { grant.Resource, grant.Qualifier, grant.Table }.All(value => Encoding.UTF8.GetByteCount(value) <= 63),
            "ssh" => (grant.Resource.Length == 0 ? !grant.ShareContent : IsServiceName(grant.Resource))
                && grant.Qualifier.Length == 0 && grant.Table.Length == 0,
            "redis" => int.TryParse(grant.Resource, NumberStyles.None, CultureInfo.InvariantCulture, out int db) && db >= 0
                && grant.Table.Length == 0 && !grant.ShareContent,
            "rabbitmq" => grant.Resource.Length > 0 && grant.Resource is not ("." or "..") && grant.Qualifier.Length == 0 && grant.Table.Length == 0 && !grant.ShareContent,
            _ => false,
        };
        if (!valid) throw Text.Exception("McpInvalidGrant");
    }

    internal static bool IsServiceName(string value) => value.Length is > 0 and <= 128
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '@' or ':' or '-');

    // 某些 HTTP SDK 会在生成 URI 时归一化相对路径；这些对象键不能作为授权前缀内的请求。
    internal static bool IsObjectPath(string value) => !value.Contains('\\') && !value.Split('/').Any(segment => segment is "." or "..")
        && !new[] { "%2e", "%2f", "%5c", "%25" }.Any(escape => value.Contains(escape, StringComparison.OrdinalIgnoreCase));

    private static bool Valid(string? value, int maximum) => value is not null && value.Length <= maximum && !value.Any(char.IsControl);
}
