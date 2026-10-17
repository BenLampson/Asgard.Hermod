namespace Asgard.Hermod.RabbitMq;

/// <summary>分页范围与对象来自同一次成功响应，失败不能推进页码。</summary>
public sealed record RabbitMqPage(IReadOnlyList<RabbitMqRecord> Items, int Page, int PageSize, int Total,
    bool LocalPaging = false)
{
    /// <summary>真实成功结果计算的页数。</summary>
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
