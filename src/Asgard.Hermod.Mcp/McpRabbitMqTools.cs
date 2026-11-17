using Asgard.Hermod.RabbitMq;

namespace Asgard.Hermod.Mcp;

/// <summary>仅走 Management API 的队列读取，不消费、重排或返回消息载荷。</summary>
internal sealed class McpRabbitMqTools(IRabbitMqSessionFactory factory)
{
    internal async Task<object> RunAsync(string name, McpGrant grant, InstanceConnectionOptions connection, McpArguments args, CancellationToken token)
    {
        await using var session = await factory.OpenAsync(connection, token).ConfigureAwait(false);
        if (name == "hermod_rabbitmq_queue_info")
        {
            string queue = args.String("queue");
            var record = await session.ReadObjectAsync(RabbitMqResource.Queues, grant.Resource, queue, token).ConfigureAwait(false);
            if (record.Vhost != grant.Resource || record.Name != queue) throw Text.Exception("McpAccessDenied");
            return new { vhost = grant.Resource, queue = Queue(record) };
        }
        var page = await session.ReadPageAsync(RabbitMqResource.Queues, grant.Resource, page: args.Integer("page", 1),
            pageSize: 25, cancellationToken: token).ConfigureAwait(false);
        return new { vhost = grant.Resource, page.Page, page.PageCount, queues = page.Items.Where(item => item.Vhost == grant.Resource).Select(Queue).ToArray() };
    }

    private static object Queue(RabbitMqRecord record) => new
    {
        record.Name,
        messages = record.Number("messages"),
        ready = record.Number("messages_ready"),
        unacknowledged = record.Number("messages_unacknowledged"),
        consumers = record.Number("consumers"),
    };
}
