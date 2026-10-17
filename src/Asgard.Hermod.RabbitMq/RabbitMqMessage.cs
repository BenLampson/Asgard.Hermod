namespace Asgard.Hermod.RabbitMq;

/// <summary>一次显式获取返回的消息快照，截断状态与本地编码切换不再访问队列。</summary>
public sealed record RabbitMqMessage(byte[] Payload, long OriginalBytes, string Exchange, string RoutingKey,
    bool Redelivered, JsonElement Properties)
{
    /// <summary>返回内容少于原始大小时，复制与导出必须标为预览。</summary>
    public bool IsTruncated => Payload.LongLength < OriginalBytes;
    /// <summary>有效 UTF-8 才作为文本展示，二进制无损回退 Base64。</summary>
    public string Display(bool base64 = false)
    {
        if (!base64)
            try { return new UTF8Encoding(false, true).GetString(Payload); }
            catch (DecoderFallbackException) { }
        return Convert.ToBase64String(Payload);
    }
}
