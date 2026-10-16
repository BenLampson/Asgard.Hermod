namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqWorkspaceViewModel
{
    public override Task InitializeAsync(CancellationToken cancellationToken = default) => _initialization ??= ConnectAsync(cancellationToken);
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _read?.Cancel(); Connection.Begin(linked.Cancel, HasConnectedOnce); Error = ""; RefreshState();
        string failure = "";
        try
        {
            if (_session is not null) await _session.DisposeAsync(); _session = null;
            var options = await _configurations.GetConnectionAsync(InstanceId!, linked.Token) ?? throw Text.Exception("RabbitMqConfigurationInvalid");
            var session = await _factory.OpenAsync(options, linked.Token);
            if (IsClosed || linked.IsCancellationRequested) { await session.DisposeAsync(); return; }
            _session = session; Endpoint = session.Options.Address; HasConnectedOnce = true;
            Navigation.Clear(); AdministrationPages.Clear();
            foreach (var item in new[] { new RabbitMqNavigation(RabbitMqResource.Overview, "layout-dashboard"), new(RabbitMqResource.Queues, "list-tree"),
                new(RabbitMqResource.Exchanges, "shuffle"), new(RabbitMqResource.Bindings, "git-branch"), new(RabbitMqResource.Connections, "cable"), new(RabbitMqResource.Channels, "waypoints") }) Navigation.Add(item);
            if (session.IsAdministrator)
                foreach (var resource in new[] { RabbitMqResource.Vhosts, RabbitMqResource.Users, RabbitMqResource.Permissions, RabbitMqResource.TopicPermissions }) AdministrationPages.Add(new(resource, "settings-2"));
            if (session.CanManagePolicies) AdministrationPages.Add(new(RabbitMqResource.Policies, "shield-check"));
            if (AdministrationPages.FirstOrDefault() is { } administration) Navigation.Add(administration with { LabelKey = "RabbitMqAdministration" });
            await RefreshScopesAsync(session, linked.Token);
            if (!Navigation.Concat(AdministrationPages).Any(item => item.Resource == Resource) || SelectedVhost is null && session.IsAdministrator) Resource = session.IsAdministrator ? RabbitMqResource.Vhosts : RabbitMqResource.Overview;
            _getUnknown = false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (_session is not null) { await _session.DisposeAsync(); _session = null; }
        }
        catch (Exception error)
        {
            failure = Text.Error(error, "RabbitMqConnectionFailed");
            if (_session is not null) { await _session.DisposeAsync(); _session = null; }
        }
        finally { Connection.Finish(failure); RefreshState(); }
        if (failure.Length == 0 && IsConnected && !linked.IsCancellationRequested)
        {
            await LoadAsync(); _refreshLoop ??= RefreshLoopAsync();
        }
    }
    private async Task RefreshLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var due = DateTimeOffset.UtcNow.AddSeconds(5);
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                if (IsActive && CanRead && !IsMessages && !Connection.IsConnecting && RefreshInterval != "off" && DateTimeOffset.UtcNow >= due)
                { await LoadAsync(true); due = DateTimeOffset.UtcNow.AddSeconds(int.Parse(RefreshInterval, CultureInfo.InvariantCulture)); }
        }
        catch (OperationCanceledException) { }
    }
    private async Task LoadAsync(bool automatic = false)
    {
        if (!IsConnected || IsDialogOpen || IsMessages || automatic && IsBusy) return;
        _read?.Cancel(); var read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _read = read;
        int revision = ++_revision; string host = ScopeVhost ?? ""; var resource = Resource; string name = _detailName;
        IsBusy = true; if (!automatic) Error = ""; RefreshState();
        try
        {
            if (ScopeVhost is null && resource is not (RabbitMqResource.Vhosts or RabbitMqResource.Users)) { Rows.Clear(); _total = 0; _detail = null; return; }
            if (IsOverview || _inDetail)
            {
                var detail = await _session!.ReadObjectAsync(resource, host, name, read.Token);
                if (revision != _revision) return;
                _detail = detail; await LoadDetailRowsAsync(read.Token);
                if (IsOverview) await LoadSummaryAsync(host, read.Token);
                if (IsOverview && _session.CanMonitor)
                {
                    var nodes = await _session.ReadPageAsync(RabbitMqResource.Nodes, cancellationToken: read.Token);
                    if (revision == _revision) { Nodes.Clear(); foreach (var item in nodes.Items) Nodes.Add(new(item)); }
                }
            }
            else
            {
                var result = await ReadListAsync(resource, host, read.Token);
                if (revision != _revision) return;
                string? selected = SelectedRow?.Identity;
                Rows.Clear(); foreach (var row in result.Items) Rows.Add(new(row));
                SelectedRow = Rows.FirstOrDefault(row => row.Identity == selected);
                _page = result.Page; _total = result.Total; _localPaging = result.LocalPaging;
                if (Search.Length == 0 && TypeFilter == "all") Navigation.FirstOrDefault(item => item.Resource == resource)?.SetCount(result.Total);
            }
            if (revision == _revision) _updated = DateTimeOffset.Now;
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        catch (Exception error) { if (revision == _revision) Fail(error); }
        finally { if (revision == _revision) { IsBusy = false; RefreshState(); } if (ReferenceEquals(_read, read)) _read = null; read.Dispose(); }
    }
    protected override async ValueTask ReleaseAsync()
    {
        _lifetime.Cancel(); _read?.Cancel(); Assistant.CloseSource();
        if (_session is not null) await _session.DisposeAsync();
        if (_refreshLoop is not null) await _refreshLoop;
        _lifetime.Dispose();
    }
}
