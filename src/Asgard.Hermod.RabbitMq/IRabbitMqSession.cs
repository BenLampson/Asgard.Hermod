namespace Asgard.Hermod.RabbitMq;

/// <summary>工作区使用的有界管理能力，不提供任意路径或持续消费接口。</summary>
public interface IRabbitMqSession : IAsyncDisposable
{
    RabbitMqSessionOptions Options { get; }
    RabbitMqRecord Identity { get; }
    string Version { get; }
    bool IsConnected { get; }
    bool IsAdministrator { get; }
    bool CanManagePolicies { get; }
    bool CanMonitor { get; }
    Task<RabbitMqPage> ReadPageAsync(RabbitMqResource resource, string vhost = "", string search = "", int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<RabbitMqRecord> ReadObjectAsync(RabbitMqResource resource, string vhost, string name, CancellationToken cancellationToken = default);
    Task<JsonElement> ExecuteAsync(RabbitMqMutation mutation, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RabbitMqMessage>> GetMessagesAsync(string vhost, string queue, int count, bool remove, CancellationToken cancellationToken = default);
}
