namespace Asgard.Hermod.UI.ViewModels;

/// <summary>管理本机 MCP 开关、默认开放的实例以及用户指定的禁止范围。</summary>
public sealed class McpSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IMcpSettingsService _service;
    private readonly Action<string> _copy;
    private readonly IMcpStorageScopeBrowser? _storageBrowser;
    private readonly CancellationTokenSource _lifetime = new();
    private McpSettingsSnapshot? _snapshot;
    private McpSettingsTarget? _selected;
    private string _resource = "", _qualifier = "", _table = "", _message = "";
    private bool _busy, _disposed;

    /// <summary>剪贴板动作可替换，视图模型不在绑定中持有客户端凭证。</summary>
    public McpSettingsViewModel(IMcpSettingsService service, Action<string>? copy = null, IMcpStorageScopeBrowser? storageBrowser = null)
    {
        _service = service; _copy = copy ?? Clipboard.SetText; _storageBrowser = storageBrowser;
        RefreshCommand = new(_ => RefreshAsync(), _ => !_disposed && !_busy);
        ToggleCommand = new(_ => ChangeAsync(async token => Apply(await _service.SetEnabledAsync(_snapshot!.Workspace.Owner, !Enabled, token)), "McpSettingsSaved"), _ => Ready);
        CopyCommand = new(_ => ChangeAsync(async token =>
        {
            string config = await _service.GetClientConfigurationAsync(_snapshot!.Workspace.Owner, token);
            if (!_disposed) _copy(config);
        }, "McpConfigurationCopied"), _ => Ready && Enabled);
        CopyPromptCommand = new(_ => ChangeAsync(_ =>
        {
            _copy(Text.Get("McpStarterPrompt"));
            return Task.CompletedTask;
        }, "McpPromptCopied", failureKey: "McpPromptCopyFailed"), _ => !_disposed && !_busy);
        AddExclusionCommand = new(_ => ChangeAsync(async token =>
        {
            var target = _selected!.Target;
            var exclusions = (_snapshot!.Exclusions ?? []).Append(new McpExclusion("", target.Id, target.TypeId, Resource, Qualifier, Table)).ToArray();
            Apply(await _service.SetExclusionsAsync(_snapshot.Workspace.Owner, exclusions, token));
            if (!_disposed) ExclusionSaved?.Invoke(this, EventArgs.Empty);
        }, "McpSettingsSaved"), _ => Ready && _selected is not null
            && (Resource.Length > 0 || Qualifier.Length == 0 && Table.Length == 0)
            && (Kind != "postgresql" || Table.Length == 0 || Qualifier.Length > 0));
        RemoveExclusionCommand = new(item => ChangeAsync(async token =>
        {
            var remaining = (_snapshot!.Exclusions ?? []).Where(exclusion => exclusion.Id != ((McpExcludedScope)item!).Id).ToArray();
            Apply(await _service.SetExclusionsAsync(_snapshot.Workspace.Owner, remaining, token));
        }, "McpSettingsSaved"), item => Ready && item is McpExcludedScope exclusion && ExcludedScopes.Contains(exclusion));
    }

    /// <summary>禁止范围成功保存后关闭编辑器。</summary>
    public event EventHandler? ExclusionSaved;
    /// <summary>当前快照的可选实例。</summary>
    public IReadOnlyList<McpSettingsTarget> Targets { get; private set; } = [];
    /// <summary>实例默认勾选，取消勾选即禁止整个实例。</summary>
    public IReadOnlyList<McpInstanceAccessViewModel> InstanceAccess { get; private set; } = [];
    /// <summary>细粒度禁止范围；整实例禁用由勾选状态表示。</summary>
    public IReadOnlyList<McpExcludedScope> ExcludedScopes { get; private set; } = [];
    /// <summary>没有已保存实例时显示说明。</summary>
    public bool NoInstances => InstanceAccess.Count == 0;
    /// <summary>没有细粒度禁止范围时显示说明。</summary>
    public bool NoExcludedScopes => ExcludedScopes.Count == 0;
    /// <summary>策略保存时暂停更改，防止覆盖并发选择。</summary>
    public bool CanEditExclusions => Ready;
    /// <summary>当前工作区的接入状态。</summary>
    public bool Enabled => _snapshot?.Enabled == true;
    /// <summary>当前会话的连接可用状态。</summary>
    public string StateText => Text.Get(Enabled ? "McpEnabled" : "McpDisabled");
    /// <summary>状态变更以保存结果为准。</summary>
    public string ToggleLabel => Text.Get(Enabled ? "McpDisable" : "McpEnable");
    /// <summary>保存进行中时弹窗不允许取消，以免误以为写入未发生。</summary>
    public bool IsBusy => _busy;
    /// <summary>只展示安全的操作反馈，不保存配对令牌。</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    /// <summary>禁止范围表单按实例类型选择字段，并在切换实例时清空草稿。</summary>
    public McpSettingsTarget? SelectedTarget
    {
        get => _selected;
        set
        {
            if (value == _selected || value is not null && !Targets.Contains(value)) return;
            _selected = value;
            _resource = Kind switch { "redis" => "0", "rabbitmq" => "/", _ => "" };
            _qualifier = Kind == "postgresql" ? "public" : "";
            _table = "";
            foreach (string property in new[] { nameof(SelectedTarget), nameof(ShowResource), nameof(ShowQualifier), nameof(ShowExclusionTable),
                nameof(ShowTransfers), nameof(CanBrowseStorage), nameof(ResourceLabel), nameof(QualifierLabel), nameof(ExclusionTableLabel),
                nameof(Resource), nameof(Qualifier), nameof(Table) }) OnPropertyChanged(property);
            AddExclusionCommand.NotifyCanExecuteChanged();
        }
    }
    /// <summary>Bucket、数据库、服务或虚拟主机的字面名称；空值代表整个实例。</summary>
    public string Resource { get => _resource; set { if (SetProperty(ref _resource, value)) AddExclusionCommand.NotifyCanExecuteChanged(); } }
    /// <summary>对象键前缀、Redis 键前缀或 PostgreSQL Schema。</summary>
    public string Qualifier { get => _qualifier; set { if (SetProperty(ref _qualifier, value)) AddExclusionCommand.NotifyCanExecuteChanged(); } }
    /// <summary>表或队列的准确名称，空值代表全部。</summary>
    public string Table { get => _table; set { if (SetProperty(ref _table, value)) AddExclusionCommand.NotifyCanExecuteChanged(); } }
    private string Kind => _selected?.Target.TypeId ?? "";
    private bool Ready => !_disposed && !_busy && _snapshot is not null;
    /// <summary>选择实例后显示对应的资源字段。</summary>
    public bool ShowResource => _selected is not null;
    /// <summary>需要前缀或 Schema 的实例显示次级范围。</summary>
    public bool ShowQualifier => Kind is "storage" or "postgresql" or "redis";
    /// <summary>数据库或消息队列可以禁止具体表或队列。</summary>
    public bool ShowExclusionTable => Kind is "mysql" or "postgresql" or "rabbitmq";
    /// <summary>对象存储展示可浏览的 Bucket 和目录。</summary>
    public bool ShowTransfers => Kind == "storage";
    /// <summary>只有快照就绪时允许浏览对象存储。</summary>
    public bool CanBrowseStorage => Ready && ShowTransfers && _storageBrowser is not null;
    /// <summary>基础范围标签兼作无障碍名称。</summary>
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
    /// <summary>范围细项按实例类型选择标签。</summary>
    public string ExclusionTableLabel => Text.Get(Kind == "rabbitmq" ? "McpApprovalQueue" : "McpExclusionTable");
    /// <summary>更新实例与禁止范围。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>开启或关闭当前工作区的本机接入。</summary>
    public AsyncRelayCommand ToggleCommand { get; }
    /// <summary>仅响应用户命令复制客户端配置。</summary>
    public AsyncRelayCommand CopyCommand { get; }
    /// <summary>复制本地化开场提示。</summary>
    public AsyncRelayCommand CopyPromptCommand { get; }
    /// <summary>添加明确的禁止范围。</summary>
    public AsyncRelayCommand AddExclusionCommand { get; }
    /// <summary>移除指定禁止范围。</summary>
    public AsyncRelayCommand RemoveExclusionCommand { get; }

    /// <summary>每次打开编辑器都从空草稿开始，取消不保存。</summary>
    public void BeginExclusionDraft() { SelectedTarget = null; Message = ""; }
    /// <summary>浏览器固定打开时的工作区和实例版本，不持有凭据。</summary>
    public McpStorageBrowserViewModel? CreateStorageBrowser() => CanBrowseStorage && _snapshot is not null && _selected is not null
        ? new(_storageBrowser!, _snapshot.Workspace.Owner, _selected.Target) : null;
    /// <summary>只把当前工作区和原实例的已确认选择写入草稿。</summary>
    public void ApplyStorageScope(McpStorageBrowserViewModel browser)
    {
        if (!Ready || _snapshot?.Workspace.Owner != browser.Owner || _selected?.Target != browser.Target || !browser.CanChoose)
        { Message = Text.Get("McpWorkspaceChanged"); return; }
        Resource = browser.SelectedBucket!.Name; Qualifier = browser.ChosenPrefix; Message = "";
    }
    /// <summary>启动设置页或切换账号时重新获取工作区；旧草稿不能跨账号提交。</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => ChangeAsync(async token => Apply(await _service.ReadAsync(token)), null, cancellationToken);

    private async Task ChangeAsync(Func<CancellationToken, Task> change, string? success, CancellationToken cancellationToken = default,
        string failureKey = "McpSettingsFailed")
    {
        if (_disposed || _busy) return;
        _busy = true; NotifyCommands();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        try { await change(linked.Token); if (!_disposed) Message = success is null ? "" : Text.Get(success); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { if (!_disposed) Message = Text.Error(error, failureKey); }
        finally { _busy = false; NotifyCommands(); }
    }

    private void Apply(McpSettingsSnapshot snapshot)
    {
        if (_disposed) return;
        bool sameOwner = _snapshot?.Workspace.Owner == snapshot.Workspace.Owner;
        var selected = _selected; _snapshot = snapshot;
        var duplicateNames = snapshot.Workspace.Targets.GroupBy(target => (target.Name, target.TypeId))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var targets = snapshot.Workspace.Targets.Select(target => new McpSettingsTarget(target,
            Text.Format(duplicateNames.Contains((target.Name, target.TypeId)) ? "McpInstanceChoiceDisambiguated" : "McpInstanceChoice",
                target.Name, TypeLabel(target.TypeId), target.Id[..Math.Min(target.Id.Length, 8)]))).ToArray();
        if (!sameOwner || !Targets.SequenceEqual(targets))
        {
            Targets = targets; OnPropertyChanged(nameof(Targets));
            SelectedTarget = sameOwner && selected is not null ? Targets.FirstOrDefault(entry => entry.Target == selected.Target) : null;
        }
        var exclusions = snapshot.Exclusions ?? [];
        InstanceAccess = Targets.Select(target => new McpInstanceAccessViewModel(target,
            !exclusions.Any(item => item.InstanceId == target.Target.Id && item.Resource.Length == 0), OnInstanceAccessChanged)).ToArray();
        ExcludedScopes = exclusions.Where(item => item.Resource.Length > 0).Select(item => new McpExcludedScope(item.Id,
            Targets.FirstOrDefault(target => target.Target.Id == item.InstanceId)?.Label ?? Text.Get("McpUnavailableInstance"), ExclusionScope(item))).ToArray();
        OnPropertyChanged(nameof(InstanceAccess)); OnPropertyChanged(nameof(NoInstances));
        OnPropertyChanged(nameof(ExcludedScopes)); OnPropertyChanged(nameof(NoExcludedScopes));
        OnPropertyChanged(nameof(Enabled)); OnPropertyChanged(nameof(ToggleLabel)); OnPropertyChanged(nameof(StateText));
    }

    private async void OnInstanceAccessChanged(McpInstanceAccessViewModel item, bool allowed)
    {
        if (!Ready || _snapshot is null) { item.Restore(!allowed); return; }
        var exclusions = (_snapshot.Exclusions ?? []).Where(exclusion => exclusion.InstanceId != item.Target.Target.Id || exclusion.Resource.Length > 0).ToList();
        if (!allowed) exclusions.Add(new("", item.Target.Target.Id, item.Target.Target.TypeId));
        await ChangeAsync(async token => Apply(await _service.SetExclusionsAsync(_snapshot!.Workspace.Owner, exclusions, token)), "McpSettingsSaved");
        if (_disposed) return;
        bool actual = !(_snapshot?.Exclusions ?? []).Any(exclusion => exclusion.InstanceId == item.Target.Target.Id && exclusion.Resource.Length == 0);
        if (actual != allowed) item.Restore(actual);
    }

    private static string ExclusionScope(McpExclusion item)
    {
        string any = Text.Get("McpExclusionAll");
        return item.TypeId switch
        {
            "storage" => Text.Format("McpExclusionStorage", item.Resource, item.Qualifier.Length == 0 ? any : item.Qualifier),
            "mysql" => Text.Format("McpExclusionMysql", item.Resource, item.Table.Length == 0 ? any : item.Table),
            "postgresql" => Text.Format("McpExclusionPostgres", item.Resource, item.Qualifier.Length == 0 ? any : item.Qualifier, item.Table.Length == 0 ? any : item.Table),
            "redis" => Text.Format("McpExclusionRedis", item.Resource, item.Qualifier.Length == 0 ? any : item.Qualifier),
            "rabbitmq" => Text.Format("McpExclusionRabbit", item.Resource, item.Table.Length == 0 ? any : item.Table),
            _ => Text.Format("McpExclusionService", item.Resource),
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
        OnPropertyChanged(nameof(CanBrowseStorage)); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanEditExclusions));
        foreach (var command in new[] { RefreshCommand, ToggleCommand, CopyCommand, CopyPromptCommand, AddExclusionCommand, RemoveExclusionCommand }) command.NotifyCanExecuteChanged();
    }
    /// <summary>关闭窗口时取消正在读写的任务，迟到结果不触碰视图。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); NotifyCommands();
    }
}
