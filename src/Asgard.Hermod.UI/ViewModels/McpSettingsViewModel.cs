namespace Asgard.Hermod.UI.ViewModels;

/// <summary>管理本机 MCP 开关、实例授权与剪贴板操作。</summary>
public sealed class McpSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IMcpSettingsService _service;
    private readonly Action<string> _copy;
    private readonly CancellationTokenSource _lifetime = new();
    private McpSettingsSnapshot? _snapshot;
    private McpSettingsTarget? _selected;
    private string _resource = "", _qualifier = "", _table = "", _message = "";
    private bool _shareContent, _busy, _disposed;

    /// <summary>剪贴板动作可替换，视图模型从不在绑定中持有可复制的客户端凭证。</summary>
    public McpSettingsViewModel(IMcpSettingsService service, Action<string>? copy = null)
    {
        _service = service;
        _copy = copy ?? Clipboard.SetText;
        RefreshCommand = new(_ => RefreshAsync(), _ => !_disposed && !_busy);
        ToggleCommand = new(_ => ChangeAsync(async token => Apply(await _service.SetEnabledAsync(_snapshot!.Workspace.Owner, !Enabled, token)), "McpSettingsSaved"), _ => Ready);
        CopyCommand = new(_ => ChangeAsync(async token =>
        {
            string config = await _service.GetClientConfigurationAsync(_snapshot!.Workspace.Owner, token);
            if (!_disposed) _copy(config);
        }, "McpConfigurationCopied"), _ => Ready && Enabled);
        CopyPromptCommand = new(_ => ChangeAsync(_ =>
        {
            // 对话提示使用固定资源，复制时不读取配对令牌或工作区内容。
            _copy(Text.Get("McpStarterPrompt"));
            return Task.CompletedTask;
        }, "McpPromptCopied", failureKey: "McpPromptCopyFailed"), _ => !_disposed && !_busy);
        GrantCommand = new(_ => ChangeAsync(async token =>
        {
            var target = _selected!.Target;
            var draft = new McpGrant("", target.Id, target.TypeId, target.Revision, Resource, Qualifier, Table, ShareContent);
            var snapshot = await _service.AddGrantAsync(_snapshot!.Workspace.Owner, draft, token);
            Apply(snapshot);
        }, "McpSettingsSaved"), _ => Ready && _selected is not null
            && (Kind == "ssh" || Resource.Length > 0) && (Kind != "postgresql" || Qualifier.Length > 0)
            && (!ShareContent || (ShowTable ? Table.Length > 0 : Kind != "ssh" || Resource.Length > 0)));
        RevokeCommand = new(item => ChangeAsync(async token => Apply(await _service.RemoveGrantAsync(_snapshot!.Workspace.Owner,
            ((McpSettingsGrant)item!).Id, token)), "McpSettingsSaved"), item => Ready && item is McpSettingsGrant grant && Grants.Contains(grant));
    }

    /// <summary>当前快照的可用授权目标。</summary>
    public IReadOnlyList<McpSettingsTarget> Targets { get; private set; } = [];
    /// <summary>保留失效记录，以便用户看见原因并移除。</summary>
    public IReadOnlyList<McpSettingsGrant> Grants { get; private set; } = [];
    /// <summary>授权表单使用实例类型选择对应的范围字段。</summary>
    public McpSettingsTarget? SelectedTarget
    {
        get => _selected;
        set
        {
            if (value == _selected || (value is not null && !Targets.Contains(value))) return;
            _selected = value;
            _resource = Kind switch { "redis" => "0", "rabbitmq" => "/", _ => "" };
            _qualifier = Kind == "postgresql" ? "public" : "";
            _table = ""; _shareContent = false;
            foreach (string property in new[] { nameof(SelectedTarget), nameof(ShowResource), nameof(ShowQualifier), nameof(ShowTable),
                nameof(ShowContent), nameof(ResourceLabel), nameof(QualifierLabel), nameof(ContentLabel), nameof(ScopeHint),
                nameof(Resource), nameof(Qualifier), nameof(Table), nameof(ShareContent) }) OnPropertyChanged(property);
            GrantCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Bucket、数据库、服务或虚拟主机的字面名称。</summary>
    public string Resource { get => _resource; set { if (SetProperty(ref _resource, value)) GrantCommand.NotifyCanExecuteChanged(); } }
    /// <summary>对象键前缀、Redis 键前缀或 PostgreSQL Schema。</summary>
    public string Qualifier { get => _qualifier; set { if (SetProperty(ref _qualifier, value)) GrantCommand.NotifyCanExecuteChanged(); } }
    /// <summary>限制到指定表；读取行时必填。</summary>
    public string Table { get => _table; set { if (SetProperty(ref _table, value)) GrantCommand.NotifyCanExecuteChanged(); } }
    /// <summary>内容须经独立的肯定选择，新增实例默认不勾选。</summary>
    public bool ShareContent { get => _shareContent; set { if (SetProperty(ref _shareContent, value)) GrantCommand.NotifyCanExecuteChanged(); } }

    private string Kind => _selected?.Target.TypeId ?? "";
    private bool Ready => !_disposed && !_busy && _snapshot is not null;
    /// <summary>快照未加载时不开放连接操作。</summary>
    public bool Enabled => _snapshot?.Enabled == true;
    /// <summary>空列表显示授权引导。</summary>
    public bool NoGrants => Grants.Count == 0;
    /// <summary>选择实例后才显示范围字段。</summary>
    public bool ShowResource => _selected is not null;
    /// <summary>仅需要字面前缀或 Schema 的连接器显示次级范围。</summary>
    public bool ShowQualifier => Kind is "storage" or "postgresql" or "redis";
    /// <summary>数据库授权可限定到某张表。</summary>
    public bool ShowTable => Kind is "mysql" or "postgresql";
    /// <summary>仅支持额外读取正文的连接器显示内容授权。</summary>
    public bool ShowContent => Kind is "storage" or "mysql" or "postgresql" or "ssh";
    /// <summary>当前会话的连接可用状态。</summary>
    public string StateText => Text.Get(Enabled ? "McpEnabled" : "McpDisabled");
    /// <summary>显示切换后的动作，状态变更以保存结果为准。</summary>
    public string ToggleLabel => Text.Get(Enabled ? "McpDisable" : "McpEnable");
    /// <summary>基础范围标签兼作字段的无障碍名称。</summary>
    public string ResourceLabel => Text.Get(Kind switch
    {
        "storage" => "McpBucket",
        "redis" => "McpRedisDatabase",
        "ssh" => "McpServiceUnit",
        "rabbitmq" => "McpVhost",
        _ => "McpDatabase",
    });
    /// <summary>次级范围字段的本地化标签。</summary>
    public string QualifierLabel => Text.Get(Kind == "postgresql" ? "McpSchema" : "McpPrefix");
    /// <summary>根据连接器说明将要共享的数据种类。</summary>
    public string ContentLabel => Text.Get(Kind switch { "storage" => "McpShareFiles", "ssh" => "McpShareLogs", _ => "McpShareRows" });
    /// <summary>说明空字段是否扩大授权范围。</summary>
    public string ScopeHint => _selected is null ? "" : Text.Get(Kind switch
    {
        "storage" => "McpStorageScopeHint",
        "redis" => "McpRedisScopeHint",
        "ssh" => "McpSshScopeHint",
        "rabbitmq" => "McpRabbitScopeHint",
        _ => "McpSqlScopeHint",
    });
    /// <summary>只展示安全的操作反馈，不保存配对令牌。</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>更新可选连接器与现有授权。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>开启或关闭当前工作区接入。</summary>
    public AsyncRelayCommand ToggleCommand { get; }
    /// <summary>仅响应用户命令复制客户端配置。</summary>
    public AsyncRelayCommand CopyCommand { get; }
    /// <summary>复制可粘贴到 AI 对话的本地化开场提示。</summary>
    public AsyncRelayCommand CopyPromptCommand { get; }
    /// <summary>把表单验证并保存为新的授权。</summary>
    public AsyncRelayCommand GrantCommand { get; }
    /// <summary>撤销指定范围的访问。</summary>
    public AsyncRelayCommand RevokeCommand { get; }

    /// <summary>启动设置页或切换账号时重新获取工作区；旧表单不能跨账号提交。</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => ChangeAsync(async token => Apply(await _service.ReadAsync(token)), null, cancellationToken);

    private async Task ChangeAsync(Func<CancellationToken, Task> change, string? success, CancellationToken cancellationToken = default,
        string failureKey = "McpSettingsFailed")
    {
        if (_disposed || _busy) return;
        _busy = true; NotifyCommands();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try
        {
            await change(linked.Token);
            if (!_disposed) Message = success is null ? "" : Text.Get(success);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) Message = Text.Error(error, failureKey); }
        finally { _busy = false; NotifyCommands(); }
    }

    private void Apply(McpSettingsSnapshot snapshot)
    {
        if (_disposed) return;
        bool sameOwner = _snapshot?.Workspace.Owner == snapshot.Workspace.Owner;
        var selected = _selected;
        _snapshot = snapshot;
        var duplicateNames = snapshot.Workspace.Targets.GroupBy(target => (target.Name, target.TypeId))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var targets = snapshot.Workspace.Targets.Select(target => new McpSettingsTarget(target,
            Text.Format(duplicateNames.Contains((target.Name, target.TypeId)) ? "McpInstanceChoiceDisambiguated" : "McpInstanceChoice",
                target.Name, TypeLabel(target.TypeId), target.Id[..Math.Min(target.Id.Length, 8)]))).ToArray();
        // 普通保存不替换下拉框内容，避免 WPF 清空选择和未提交草稿。
        if (!sameOwner || !Targets.SequenceEqual(targets))
        {
            Targets = targets;
            OnPropertyChanged(nameof(Targets));
            SelectedTarget = sameOwner && selected is not null ? Targets.FirstOrDefault(entry => entry.Target == selected.Target) : null;
        }
        Grants = snapshot.Grants.Select(grant =>
        {
            var target = Targets.FirstOrDefault(entry => entry.Target.Id == grant.InstanceId);
            return new McpSettingsGrant(grant.Id, target?.Label ?? Text.Get("McpUnavailableInstance"), Scope(grant),
                target?.Target.Revision == grant.Revision && target.Target.TypeId == grant.TypeId);
        }).ToArray();
        OnPropertyChanged(nameof(Grants)); OnPropertyChanged(nameof(NoGrants));
        OnPropertyChanged(nameof(Enabled)); OnPropertyChanged(nameof(ToggleLabel)); OnPropertyChanged(nameof(StateText));
    }

    private static string Scope(McpGrant grant)
    {
        string table = grant.Table.Length == 0 ? Text.Get("McpAllTables") : grant.Table;
        string prefix = grant.Qualifier.Length == 0 ? Text.Get("McpAnyPrefix") : grant.Qualifier;
        string content = Text.Get(grant.ShareContent ? "McpContentShared" : "McpMetadataOnly");
        return grant.TypeId switch
        {
            "storage" => Text.Format("McpStorageScope", grant.Resource, prefix, content),
            "mysql" => Text.Format("McpMysqlScope", grant.Resource, table, content),
            "postgresql" => Text.Format("McpPostgresScope", grant.Resource, grant.Qualifier, table, content),
            "ssh" => grant.Resource.Length > 0 ? Text.Format("McpSshServiceScope", grant.Resource, content) : Text.Get("McpHostScope"),
            "redis" => Text.Format("McpRedisScope", grant.Resource, prefix),
            _ => Text.Format("McpRabbitScope", grant.Resource),
        };
    }

    private static string TypeLabel(string type) => Text.Get(type switch
    {
        "storage" => "ObjectStorage",
        "ssh" => "McpTypeSsh",
        "mysql" => "McpTypeMysql",
        "postgresql" => "McpTypePostgres",
        "redis" => "McpTypeRedis",
        _ => "McpTypeRabbitMq",
    });

    private void NotifyCommands()
    {
        foreach (var command in new[] { RefreshCommand, ToggleCommand, CopyCommand, CopyPromptCommand, GrantCommand, RevokeCommand }) command.NotifyCanExecuteChanged();
    }

    /// <summary>关闭窗口时取消正在读写的授权任务，迟到结果不触碰视图。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); NotifyCommands();
    }
}
