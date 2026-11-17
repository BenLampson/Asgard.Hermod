using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp;

/// <summary>每个工具绑定允许的连接器与可选内容权限；模型不能覆盖这两个条件。</summary>
internal sealed record McpToolDefinition(string Name, string DescriptionKey, string[] Types, bool ShareContent, McpArgument[] Arguments)
{
    internal bool Allows(McpGrant grant) => Types.Contains(grant.TypeId, StringComparer.Ordinal) && (!ShareContent || grant.ShareContent);

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
        Annotations = new() { ReadOnlyHint = true, DestructiveHint = false, OpenWorldHint = Types.Length > 0 },
    };
}
