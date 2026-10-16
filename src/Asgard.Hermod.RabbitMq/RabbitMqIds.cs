namespace Asgard.Hermod.RabbitMq;

/// <summary>消息队列分类与 RabbitMQ 产品、连接方式各自使用稳定标识。</summary>
public static class RabbitMqIds
{
    /// <summary>保存实例使用产品标识，分类不替换已有实例身份。</summary>
    public const string Type = "rabbitmq";
    /// <summary>直接连接管理 API。</summary>
    public const string Direct = "rabbitmq.direct";
    /// <summary>经现有 SSH 信任流程连接管理 API。</summary>
    public const string Ssh = "rabbitmq.ssh";
    /// <summary>公共 HTTP 工厂名称。</summary>
    public const string HttpClient = "Hermod.RabbitMQ";
}
