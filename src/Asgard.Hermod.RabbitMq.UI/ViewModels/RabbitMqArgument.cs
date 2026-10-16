namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>AMQP 参数和头部保留数值、布尔及嵌套 JSON 类型。</summary>
public sealed class RabbitMqArgument : ObservableObject
{
    public string Key { get; set => SetProperty(ref field, value); } = "";
    public string Type { get; set => SetProperty(ref field, value); } = "string";
    public string Value { get; set => SetProperty(ref field, value); } = "";
    public IReadOnlyList<InstanceFieldChoice> Types { get; } = [new("string", Text.Get("RabbitMqString")), new("number", Text.Get("RabbitMqNumber")), new("boolean", Text.Get("RabbitMqBoolean")), new("json", "JSON")];
    /// <summary>不按区域文化猜测协议数值，拒绝重复键和不完整行。</summary>
    public static Dictionary<string, object?> Build(IEnumerable<RabbitMqArgument> rows)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Key.Length == 0 && row.Value.Length == 0) continue;
            if (row.Key.Length == 0 || result.ContainsKey(row.Key)) throw Text.Exception("RabbitMqArgumentInvalid");
            object? value;
            try
            {
                value = row.Type switch
                {
                    "string" => row.Value,
                    "number" => decimal.Parse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
                    "boolean" => bool.Parse(row.Value),
                    "json" => JsonSerializer.Deserialize<JsonElement>(row.Value),
                    _ => throw new FormatException(),
                };
            }
            catch (Exception error) when (error is FormatException or OverflowException or JsonException) { throw Text.Exception("RabbitMqArgumentInvalid"); }
            result.Add(row.Key, value);
        }
        return result;
    }
    /// <summary>编辑时保留未知扩展参数及类型。</summary>
    public static IEnumerable<RabbitMqArgument> From(JsonElement data) => data.ValueKind == JsonValueKind.Object
        ? data.EnumerateObject().Select(item => new RabbitMqArgument
        {
            Key = item.Name,
            Type = item.Value.ValueKind switch { JsonValueKind.Number => "number", JsonValueKind.True or JsonValueKind.False => "boolean", JsonValueKind.String => "string", _ => "json" },
            Value = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? "" : item.Value.GetRawText()
        }) : [];
}
