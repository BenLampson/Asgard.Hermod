namespace Asgard.Hermod.RabbitMq;

/// <summary>显式修改的封闭集合，禁止外部文本指定任意管理 API 地址。</summary>
public enum RabbitMqOperation
{
    DeclareQueue, DeclareExchange, CreateBinding, DeleteQueue, DeleteExchange,
    DeleteBinding, PurgeQueue, CloseConnection, PutVhost, DeleteVhost, PutUser,
    DeleteUser, PutPermissions, DeletePermissions, PutTopicPermissions,
    DeleteTopicPermissions, PutPolicy, DeletePolicy, Publish,
}
