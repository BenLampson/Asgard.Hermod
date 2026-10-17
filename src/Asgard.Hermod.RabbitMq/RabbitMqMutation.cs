namespace Asgard.Hermod.RabbitMq;

/// <summary>原生确认窗口提交的不可变目标和参数；不允许服务端回复改变目标。</summary>
public sealed record RabbitMqMutation(RabbitMqOperation Operation, string Vhost, string Name,
    JsonElement Body = default, string Destination = "", string DestinationType = "queue",
    string PropertiesKey = "", bool OnlyEmpty = true, bool OnlyUnused = true);
