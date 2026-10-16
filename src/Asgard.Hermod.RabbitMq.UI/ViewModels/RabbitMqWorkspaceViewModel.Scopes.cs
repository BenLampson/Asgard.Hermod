namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqWorkspaceViewModel
{
    private bool _refreshingScopes;

    // 先读取完整集合，再替换可见范围；管理员身份本身不授予资源访问权。
    private async Task RefreshScopesAsync(IRabbitMqSession session, CancellationToken token)
    {
        _refreshingScopes = true;
        RefreshState();
        try
        {
            var hosts = new List<string>();
            for (int page = 1; page <= 100; page++)
            {
                var result = await session.ReadPageAsync(RabbitMqResource.Vhosts, page: page, pageSize: 100, cancellationToken: token);
                hosts.AddRange(result.Items.Select(item => item.Name));
                if (page >= result.PageCount) break;
            }
            var allowed = new HashSet<string>(hosts, StringComparer.Ordinal);
            if (session.IsAdministrator)
            {
                var permissions = new HashSet<string>(StringComparer.Ordinal);
                for (int page = 1; page <= 100; page++)
                {
                    var result = await session.ReadPageAsync(RabbitMqResource.OwnPermissions, page: page, pageSize: 100, cancellationToken: token);
                    permissions.UnionWith(result.Items.Select(item => item.Vhost));
                    if (page >= result.PageCount) break;
                }
                allowed.IntersectWith(permissions);
            }
            string? previous = SelectedVhost, previousAdministration = AdministrationVhost;
            AdministrationVhosts.Clear(); Vhosts.Clear();
            foreach (string host in hosts) { AdministrationVhosts.Add(host); if (allowed.Contains(host)) Vhosts.Add(host); }
            SelectedVhost = previous is not null && Vhosts.Contains(previous) ? previous : Vhosts.FirstOrDefault();
            AdministrationVhost = previousAdministration is not null && AdministrationVhosts.Contains(previousAdministration) ? previousAdministration : AdministrationVhosts.FirstOrDefault();
        }
        finally { _refreshingScopes = false; RefreshState(); }
    }
}
