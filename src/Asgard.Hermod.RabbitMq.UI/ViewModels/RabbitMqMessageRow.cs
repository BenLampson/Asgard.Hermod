namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>不可变消息预览，不以消息编号伪装服务端消费游标。</summary>
public sealed record RabbitMqMessageRow(int Number, RabbitMqMessage Message)
{
    public string Label => Text.Format("RabbitMqMessageNumber", Number);
    public string Summary => Text.Format("RabbitMqMessageBytes", Message.Payload.Length, Message.OriginalBytes);
}
