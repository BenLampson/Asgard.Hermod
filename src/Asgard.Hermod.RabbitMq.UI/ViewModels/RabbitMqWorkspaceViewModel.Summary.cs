namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqWorkspaceViewModel
{
    /// <summary>概览中的资源计数来自相同 vhost 的 API，不使用全局集群总数。</summary>
    public ObservableCollection<RabbitMqRow> SummaryRows { get; } = [];
    /// <summary>资源概况列。</summary>
    public IReadOnlyList<RabbitMqColumn> SummaryColumns { get; } = [new("name", "RabbitMqResource", 0), new("count", "RabbitMqCount", 200), new("scope", "RabbitMqScopeLabel", 400)];
    /// <summary>对象属性表列。</summary>
    public IReadOnlyList<RabbitMqColumn> PropertyColumns { get; } = [new("name", "RabbitMqProperty", 0), new("value", "RabbitMqValue", 550)];
    /// <summary>详情概览仅展示明确的服务器属性，原始参数保留在参数页。</summary>
    public IReadOnlyList<RabbitMqRow> PropertyRows
    {
        get
        {
            if (_detail is null) return [];
            var row = new RabbitMqRow(_detail);
            RabbitMqRow Pair(string label, string value) => new(new(JsonSerializer.SerializeToElement(new { name = Text.Get(label), value })));
            if (Resource == RabbitMqResource.Queues)
                return [Pair("RabbitMqNode", row["node"]), Pair("RabbitMqDurability", Text.Format("RabbitMqPair", row["durable"], row["auto_delete"])),
                    Pair("RabbitMqPolicy", row["policy"]), Pair("RabbitMqMessageTtl", row["arguments.x-message-ttl"]),
                    Pair("RabbitMqDeadLetterExchange", row["arguments.x-dead-letter-exchange"]), Pair("RabbitMqDeadLetterRoutingKey", row["arguments.x-dead-letter-routing-key"])];
            if (Resource == RabbitMqResource.Exchanges)
                return [Pair("RabbitMqType", row["type"]), Pair("RabbitMqDurable", row["durable"]), Pair("RabbitMqAutoDelete", row["auto_delete"]), Pair("RabbitMqInternal", row["internal"]), Pair("RabbitMqPolicy", row["policy"])];
            return [Pair("RabbitMqUser", row["user"]), Pair("RabbitMqState", row["state"]), Pair("RabbitMqNode", row["node"]), Pair("RabbitMqProtocol", row["protocol"])];
        }
    }
    /// <summary>每种属性占一行；有界布局不产生大块空白表格。</summary>
    public double PropertiesHeight => (PropertyRows.Count + 1) * 36 + 2;
    /// <summary>刷新按钮也在没有返回按钮的概览中可见。</summary>
    public bool HasHeaderRefresh => !IsList;
    /// <summary>当前采样速率作为柱图说明，不填充虚构数据。</summary>
    public string RateSummary => _detail is null ? "" : Text.Format("RabbitMqRateSummary", new RabbitMqRow(_detail)["message_stats.publish_details.rate"], new RabbitMqRow(_detail)["message_stats.deliver_get_details.rate"]);

    private async Task LoadSummaryAsync(string host, CancellationToken token)
    {
        int revision = _revision;
        var rows = new List<RabbitMqRow>();
        foreach (var resource in new[] { RabbitMqResource.Queues, RabbitMqResource.Exchanges, RabbitMqResource.Connections, RabbitMqResource.Channels, RabbitMqResource.Consumers })
        {
            string count;
            try { count = (await _session!.ReadPageAsync(resource, host, pageSize: 1, cancellationToken: token)).Total.ToString("N0", CultureInfo.CurrentCulture); }
            catch (RabbitMqFailure error) when (error.ResourceKey == "RabbitMqPermissionDenied") { count = Text.Get("RabbitMqUnavailable"); }
            if (revision == _revision && int.TryParse(count, NumberStyles.Number, CultureInfo.CurrentCulture, out int total)) Navigation.FirstOrDefault(item => item.Resource == resource)?.SetCount(total);
            rows.Add(new(new(JsonSerializer.SerializeToElement(new { name = Text.Get("RabbitMq" + resource), count, scope = host }))));
        }
        if (revision != _revision) return;
        SummaryRows.Clear(); foreach (var row in rows) SummaryRows.Add(row);
    }
}
