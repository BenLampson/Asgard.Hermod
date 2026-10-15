namespace Asgard.Hermod.Agent;

/// <summary>进程内模型配置容器；调用方只持久化地址和模型，密钥永不落盘。</summary>
public sealed class AgentConfiguration
{
    private AgentModelOptions _current = new();
    /// <summary>原子读取不可变快照，设置变更不会把请求的地址和密钥混合。</summary>
    public AgentModelOptions Current => Volatile.Read(ref _current);

    /// <summary>校验后原子替换配置；空密钥表示不附带认证头。</summary>
    public void Update(string endpoint, string model, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint); ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(apiKey);
        var options = new AgentModelOptions(endpoint, model, apiKey);
        options.Validate(allowEmpty: true);
        Interlocked.Exchange(ref _current, options);
    }
}
