using System.Text.Json;

namespace Asgard.Hermod.Mcp;

/// <summary>在解密或建立连接前验证所有字段；未知字段、越界和类型不匹配都被拒绝。</summary>
internal sealed class McpArguments(IReadOnlyDictionary<string, JsonElement> arguments)
{
    internal string String(string key, string fallback = "") => arguments.TryGetValue(key, out var value) ? value.GetString()! : fallback;
    internal McpArguments WithString(string key, string value)
    {
        var copy = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal) { [key] = JsonSerializer.SerializeToElement(value) };
        return new(copy);
    }
    internal int Integer(string key, int fallback) => arguments.TryGetValue(key, out var value) ? value.GetInt32() : fallback;
    internal string[] Strings(string key) => arguments.TryGetValue(key, out var value)
        ? value.EnumerateArray().Select(item => item.GetString()!).ToArray() : [];

    internal static McpArguments Validate(IReadOnlyList<McpArgument> schema, IReadOnlyDictionary<string, JsonElement>? values)
    {
        values ??= new Dictionary<string, JsonElement>();
        if (values.Count > schema.Count || values.Keys.Any(key => !schema.Any(argument => argument.Name == key)))
            throw Text.Exception("McpInvalidArguments");
        foreach (var argument in schema)
        {
            if (!values.TryGetValue(argument.Name, out var value))
            {
                if (argument.Required) throw Text.Exception("McpInvalidArguments");
                continue;
            }
            bool valid = argument.MaximumItems > 0
                ? value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= argument.MaximumItems
                    && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text
                        && text.Length <= argument.MaximumLength && !text.Any(char.IsControl))
                : argument.MaximumLength > 0
                ? value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= argument.MaximumLength
                    && (!argument.Required || text.Length > 0) && !text.Any(char.IsControl)
                    && (argument.Choices is null || argument.Choices.Contains(text, StringComparer.Ordinal))
                : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
                    && number >= argument.Minimum && number <= argument.Maximum
                    && (argument.IntegerChoices is null || argument.IntegerChoices.Contains(number));
            if (!valid) throw Text.Exception("McpInvalidArguments");
        }
        return new(values);
    }
}
