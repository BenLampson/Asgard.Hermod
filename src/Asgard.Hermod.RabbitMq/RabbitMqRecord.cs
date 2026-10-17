namespace Asgard.Hermod.RabbitMq;

/// <summary>只读资源快照，协议数字和原始名称独立于显示语言。</summary>
public sealed record RabbitMqRecord(JsonElement Data)
{
    /// <summary>按字段路径读取字符串，缺失字段不伪造数值。</summary>
    public string String(string path, string fallback = "")
    {
        var value = Value(path);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    }
    /// <summary>读取可空协议数值。</summary>
    public double? Number(string path) => Value(path) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out double number) ? number : null;
    /// <summary>读取布尔声明属性。</summary>
    public bool Boolean(string path) => Value(path).ValueKind == JsonValueKind.True;
    /// <summary>读取对象树中的字段，不解析显示文本。</summary>
    public JsonElement Value(string path)
    {
        var value = Data;
        foreach (string part in path.Split('.'))
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(part, out value)) return default;
        return value;
    }
    /// <summary>服务器原始名称。</summary>
    public string Name => String("name");
    /// <summary>服务器原始虚拟主机。</summary>
    public string Vhost => String("vhost");
    /// <summary>绑定标识包含参数生成的 properties_key，不能只用路由键。</summary>
    public string Identity => Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty("source", out _)
        ? JsonSerializer.Serialize(new[] { Vhost, String("source"), String("destination_type"), String("destination"), String("properties_key") })
        : JsonSerializer.Serialize(new[] { Vhost, Name, String("user"), String("exchange") });
}
