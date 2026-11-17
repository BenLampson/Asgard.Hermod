using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp;

/// <summary>只序列化显式投影，限制结果大小，并保持协议名称、数字和日期稳定。</summary>
internal static class McpJson
{
    internal static JsonSerializerOptions Schema { get; } = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly JsonSerializerOptions Results = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static CallToolResult Success(object result)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, Results);
        if (bytes.Length > 64 * 1024) throw Text.Exception("McpResultTooLarge");
        using var document = JsonDocument.Parse(bytes);
        var data = document.RootElement.Clone();
        return new() { StructuredContent = data, Content = [new TextContentBlock { Text = data.GetRawText() }] };
    }

    internal static CallToolResult Error(string message) => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    internal static string Clip(string value, int max) => value.Length <= max ? value
        : value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];
}
