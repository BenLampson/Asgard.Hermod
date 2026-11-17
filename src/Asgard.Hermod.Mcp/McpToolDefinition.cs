using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp;

/// <summary>每个工具绑定连接器、内容和传输权限；模型不能改变可用条件。</summary>
internal sealed record McpToolDefinition(string Name, string DescriptionKey, string[] Types, bool ShareContent, McpArgument[] Arguments,
    bool RequiresTransfer = false, bool ReadOnly = true, bool LocalOnly = false,
    bool RequiresCreateFolder = false, bool RequiresDelete = false, bool RequiresSignedLinks = false, bool Destructive = false)
{
    internal bool Allows(McpGrant grant) => Types.Contains(grant.TypeId, StringComparer.Ordinal) && (!ShareContent || grant.ShareContent)
        && (!RequiresTransfer || McpGrantRules.CanTransfer(grant)) && (!RequiresCreateFolder || grant.AllowCreateFolder)
        && (!RequiresDelete || grant.AllowDelete) && (!RequiresSignedLinks || grant.AllowSignedLinks);

    internal Tool ToProtocol() => new()
    {
        Name = Name,
        Description = Text.Get(DescriptionKey),
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = Arguments.ToDictionary(argument => argument.Name, argument => argument.Schema),
            required = Arguments.Where(argument => argument.Required).Select(argument => argument.Name),
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
