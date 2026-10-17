namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>表格列声明不携带视图或协议行为。</summary>
public sealed record RabbitMqColumn(string Key, string ResourceKey, double Width = 120)
{
    /// <summary>按当前启动语言创建表头。</summary>
    public string Title => Text.Get(ResourceKey);
    /// <summary>每种资源只展示原稿要求的主要字段。</summary>
    public static IReadOnlyList<RabbitMqColumn> For(RabbitMqResource resource) => resource switch
    {
        RabbitMqResource.Queues => [new("name", "RabbitMqName", 0), new("type", "RabbitMqType", 100), new("state", "RabbitMqState", 84), new("messages_ready", "RabbitMqReady", 84), new("messages_unacknowledged", "RabbitMqUnacked", 88), new("consumers", "RabbitMqConsumers", 92), new("message_stats.publish_details.rate", "RabbitMqIncoming", 84), new("message_stats.deliver_get_details.rate", "RabbitMqOutgoing", 84)],
        RabbitMqResource.Exchanges => [new("name", "RabbitMqName", 320), new("type", "RabbitMqType"), new("durable", "RabbitMqDurable"), new("auto_delete", "RabbitMqAutoDelete"), new("internal", "RabbitMqInternal"), new("message_stats.publish_in_details.rate", "RabbitMqPublishRate")],
        RabbitMqResource.Bindings => [new("source", "RabbitMqSource", 230), new("destination", "RabbitMqDestination", 230), new("destination_type", "RabbitMqType"), new("routing_key", "RabbitMqRoutingKey", 200), new("arguments", "RabbitMqArguments", 260)],
        RabbitMqResource.Connections => [new("name", "RabbitMqName", 300), new("user", "RabbitMqUser"), new("state", "RabbitMqState"), new("channels", "RabbitMqChannels", 85), new("protocol", "RabbitMqProtocol"), new("ssl", "RabbitMqTls", 85), new("recv_oct_details.rate", "RabbitMqReceiveRate"), new("send_oct_details.rate", "RabbitMqSendRate")],
        RabbitMqResource.Channels => [new("name", "RabbitMqName", 300), new("user", "RabbitMqUser"), new("state", "RabbitMqState"), new("consumer_count", "RabbitMqConsumers"), new("messages_unacknowledged", "RabbitMqUnacked"), new("prefetch_count", "RabbitMqPrefetch")],
        RabbitMqResource.Vhosts => [new("name", "RabbitMqName", 300), new("description", "RabbitMqDescription", 260), new("default_queue_type", "RabbitMqDefaultQueueType", 180), new("messages", "RabbitMqTotal"), new("tags", "RabbitMqTags", 180)],
        RabbitMqResource.Users => [new("name", "RabbitMqUser", 300), new("tags", "RabbitMqRoles", 350)],
        RabbitMqResource.Permissions => [new("user", "RabbitMqUser", 220), new("configure", "RabbitMqConfigurePattern", 260), new("write", "RabbitMqWritePattern", 260), new("read", "RabbitMqReadPattern", 260)],
        RabbitMqResource.TopicPermissions => [new("user", "RabbitMqUser", 220), new("exchange", "RabbitMqExchange", 220), new("write", "RabbitMqWritePattern", 260), new("read", "RabbitMqReadPattern", 260)],
        RabbitMqResource.Policies => [new("name", "RabbitMqName", 220), new("pattern", "RabbitMqPattern", 230), new("apply-to", "RabbitMqApplyTo", 160), new("priority", "RabbitMqPriority", 85), new("definition", "RabbitMqDefinition", 300)],
        RabbitMqResource.Nodes => [new("name", "RabbitMqName", 280), new("running", "RabbitMqState"), new("mem_used", "RabbitMqMemory"), new("disk_free", "RabbitMqDisk"), new("fd_used", "RabbitMqFileDescriptors")],
        _ => [new("consumer_tag", "RabbitMqConsumerTag", 250), new("channel_details.name", "RabbitMqChannel", 250), new("ack_required", "RabbitMqAcknowledgements"), new("prefetch_count", "RabbitMqPrefetch")],
    };
}
