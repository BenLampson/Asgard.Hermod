namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqWorkspaceViewModel
{
    private string _typeFilter = "all";
    public bool HasTypeFilter => Resource is RabbitMqResource.Queues or RabbitMqResource.Exchanges;
    public IReadOnlyList<InstanceFieldChoice> TypeFilters => new[] { new InstanceFieldChoice("all", Text.Get("RabbitMqAllTypes")) }
        .Concat((Resource == RabbitMqResource.Queues ? new[] { "classic", "quorum", "stream" } : new[] { "direct", "fanout", "topic", "headers" })
            .Select(type => new InstanceFieldChoice(type, Text.Get("RabbitMqChoice" + type)))).ToArray();
    public string TypeFilter
    {
        get => _typeFilter;
        set { if (!TypeFilters.Any(item => item.Id == value) || !SetProperty(ref _typeFilter, value)) return; _page = 1; if (CanRead) _ = LoadAsync(); }
    }
    public IReadOnlyList<InstanceFieldChoice> RefreshIntervals { get; } = [new("off", Text.Get("RabbitMqManualRefresh")), new("5", Text.Format("RabbitMqRefreshInterval", 5)), new("10", Text.Format("RabbitMqRefreshInterval", 10)), new("30", Text.Format("RabbitMqRefreshInterval", 30))];
    public string RefreshInterval { get; set { if (value is not ("off" or "5" or "10" or "30") || !SetProperty(ref field, value)) return; OnPropertyChanged(nameof(UpdatedText)); } } = "5";
    private async Task<RabbitMqPage> ReadListAsync(RabbitMqResource resource, string host, CancellationToken token)
    {
        string search = Search.Trim(); string type = TypeFilter;
        if (!HasTypeFilter || type == "all") return await _session!.ReadPageAsync(resource, host, search, _page, PageSize, token);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(TimeSpan.FromSeconds(30));
        var rows = new List<RabbitMqRecord>();
        for (int page = 1; page <= 100; page++)
        {
            var result = await _session!.ReadPageAsync(resource, host, search, page, 100, bounded.Token);
            if (result.Total > 10000) throw Text.Exception("RabbitMqResponseTooLarge");
            rows.AddRange(result.Items.Where(item => item.String("type") == type));
            if (page >= result.PageCount) break;
        }
        int effective = Math.Clamp(_page, 1, Math.Max(1, (int)Math.Ceiling(rows.Count / (double)PageSize)));
        return new(rows.Skip((effective - 1) * PageSize).Take(PageSize).ToArray(), effective, PageSize, rows.Count, true);
    }
}
