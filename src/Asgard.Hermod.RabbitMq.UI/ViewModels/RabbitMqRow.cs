namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>不可变列表快照；缺少指标展示破折号，不能假装零值。</summary>
public sealed record RabbitMqRow(RabbitMqRecord Record)
{
    /// <summary>服务端稳定身份。</summary>
    public string Identity => Record.Identity;
    /// <summary>表格安全投影。</summary>
    public string this[string key]
    {
        get
        {
            var value = Record.Value(key);
            if (key is "name" or "source" or "exchange" && value.ValueKind == JsonValueKind.String && value.GetString() == "") return Text.Get("RabbitMqDefaultExchange");
            if (value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null) return Text.Get("RabbitMqUnavailable");
            if (value.ValueKind == JsonValueKind.True) return Text.Get("RabbitMqYes");
            if (value.ValueKind == JsonValueKind.False) return Text.Get("RabbitMqNo");
            if (key == "state") return value.GetString() switch { "running" => Text.Get("RabbitMqRunning"), "idle" => Text.Get("RabbitMqIdle"), "down" => Text.Get("RabbitMqDown"), _ => value.ToString() };
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)) return number.ToString(key.EndsWith("rate", StringComparison.Ordinal) ? "N1" : "N0", CultureInfo.CurrentCulture);
            if (value.ValueKind == JsonValueKind.Array) return string.Join(", ", value.EnumerateArray().Select(item => item.ToString()));
            return value.ToString();
        }
    }
}
