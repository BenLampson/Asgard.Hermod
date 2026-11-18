using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp;

/// <summary>每个工具绑定连接器、内容和传输权限；模型不能改变可用条件。</summary>
internal sealed record McpToolDefinition(string Name, string DescriptionKey, string[] Types, bool ShareContent, McpArgument[] Arguments,
    bool RequiresTransfer = false, bool ReadOnly = true, bool LocalOnly = false,
    bool RequiresCreateFolder = false, bool RequiresDelete = false, bool RequiresSignedLinks = false, bool Destructive = false,
    bool DefaultOnly = false)
{
    internal bool Allows(McpGrant grant) => Types.Contains(grant.TypeId, StringComparer.Ordinal) && (!ShareContent || grant.ShareContent)
        && (!RequiresTransfer || McpGrantRules.CanTransfer(grant)) && (!RequiresCreateFolder || grant.AllowCreateFolder)
        && (!RequiresDelete || grant.AllowDelete) && (!RequiresSignedLinks || grant.AllowSignedLinks);

    internal IReadOnlyList<McpArgument> ArgumentsFor(bool defaultAllow)
    {
        if (!defaultAllow || Types.Length == 0) return Arguments;
        var additional = new List<McpArgument>();
        if (Types.Contains("storage")) additional.Add(new("bucket", "McpArgAccessBucket", MaximumLength: 253));
        if (Types.Any(type => type is "mysql" or "postgresql" or "redis")) additional.Add(new("database", "McpArgAccessDatabase", MaximumLength: 253));
        if (Types.Contains("postgresql")) additional.Add(new("schema", "McpArgAccessSchema", MaximumLength: 128));
        if (Types.Contains("rabbitmq")) additional.Add(new("vhost", "McpArgAccessVhost", MaximumLength: 253));
        if (Types.Contains("ssh")) additional.Add(new("service", "McpArgAccessService", MaximumLength: 128));
        return [.. Arguments, .. additional.Where(item => !Arguments.Any(existing => existing.Name == item.Name))];
    }

    internal Tool ToProtocol(bool defaultAllow = false) => new()
    {
        Name = Name,
        Description = Text.Get(DescriptionKey),
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = ArgumentsFor(defaultAllow).ToDictionary(argument => argument.Name, argument => argument.Schema),
            required = ArgumentsFor(defaultAllow).Where(argument => argument.Required).Select(argument => argument.Name),
            additionalProperties = false,
        }, McpJson.Schema),
        Annotations = new()
        {
            ReadOnlyHint = ReadOnly,
            DestructiveHint = Destructive,
            IdempotentHint = ReadOnly ? null : false,
            OpenWorldHint = Types.Length > 0 && !LocalOnly,
        },
    };
}
