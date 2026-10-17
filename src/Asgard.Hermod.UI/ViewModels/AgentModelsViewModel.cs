using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>设置中的命名模型列表，目录变化后刷新，删除需要明确的第二次确认。</summary>
public sealed class AgentModelsViewModel : ObservableObject, IDisposable
{
    private readonly AgentModelCatalog _catalog;
    private readonly IAgentClient _client;
    private readonly System.Threading.SynchronizationContext? _context = System.Threading.SynchronizationContext.Current;
    private bool _disposed;
    /// <summary>只展示当前账号目录；实际保护和同步由目录服务负责。</summary>
    public AgentModelsViewModel(AgentModelCatalog catalog, IAgentClient client)
    {
        _catalog = catalog; _client = client;
        AddCommand = new RelayCommand(_ => EditRequested?.Invoke(this, new(null, catalog.Epoch)), _ => !IsBusy && Rows.Count < 32 && catalog.Error.Length == 0);
        EditCommand = new RelayCommand(value => { if (value is AgentModelRow row) EditRequested?.Invoke(this, new(row.Profile, row.Epoch)); }, _ => !IsBusy);
        DeleteCommand = new RelayCommand(value => PendingDelete = value as AgentModelRow, _ => !IsBusy);
        CancelDeleteCommand = new RelayCommand(_ => PendingDelete = null, _ => !IsBusy);
        ConfirmDeleteCommand = new AsyncRelayCommand(_ => ChangeAsync(async () =>
        {
            if (PendingDelete is { } row) await catalog.DeleteAsync(row.Profile, row.Epoch);
            PendingDelete = null;
        }), _ => PendingDelete is not null && !IsBusy);
        DefaultCommand = new AsyncRelayCommand(value => value is AgentModelRow row ? ChangeAsync(() => catalog.SetDefaultAsync(row.Profile, row.Epoch)) : Task.CompletedTask,
            value => !IsBusy && value is AgentModelRow { IsDefault: false });
        catalog.Changed += OnChanged; Reload();
    }
    /// <summary>窗口宿主仅在用户点击后打开编辑器。</summary>
    public event EventHandler<AgentModelEditRequest>? EditRequested;
    /// <summary>不包含密钥的列表行。</summary>
    public ObservableCollection<AgentModelRow> Rows { get; } = [];
    /// <summary>当前没有已保存配置。</summary>
    public bool IsEmpty => Rows.Count == 0;
    /// <summary>串行保存状态。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) RefreshCommands(); } }
    /// <summary>待确认删除，不立即改变配置。</summary>
    public AgentModelRow? PendingDelete { get; private set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(HasPendingDelete)); OnPropertyChanged(nameof(DeletePrompt)); RefreshCommands(); } } }
    /// <summary>控制内嵌删除确认。</summary>
    public bool HasPendingDelete => PendingDelete is not null;
    /// <summary>完整可重排的删除提示。</summary>
    public string DeletePrompt => Text.Format("AgentModelDeletePrompt", PendingDelete?.Name ?? "");
    /// <summary>安全保存或读取错误。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>同步状态来自真实服务，未解密时明确锁定。</summary>
    public string SyncLabel => Text.Get(_catalog.Error.Length > 0 ? "AgentModelsLocked" : _catalog.SyncStatus switch
    {
        ConfigurationSyncStatus.Local => "AgentModelsLocal",
        ConfigurationSyncStatus.Synced => "AgentModelsSynced",
        ConfigurationSyncStatus.PasswordRequired => "AgentModelsLocked",
        ConfigurationSyncStatus.Failed => "AgentModelsSyncFailed",
        _ => "AgentModelsPending",
    });
    /// <summary>只有服务已确认完整同步时才呈现成功颜色。</summary>
    public bool IsSynced => _catalog.Error.Length == 0 && _catalog.SyncStatus == ConfigurationSyncStatus.Synced;
    /// <summary>新增配置。</summary>
    public RelayCommand AddCommand { get; }
    /// <summary>编辑配置。</summary>
    public RelayCommand EditCommand { get; }
    /// <summary>准备删除。</summary>
    public RelayCommand DeleteCommand { get; }
    /// <summary>撤销未确认删除。</summary>
    public RelayCommand CancelDeleteCommand { get; }
    /// <summary>明确删除并传播。</summary>
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    /// <summary>只修改新会话默认值。</summary>
    public AsyncRelayCommand DefaultCommand { get; }
    /// <summary>编辑器捕获当前账号与原记录版本。</summary>
    public AgentModelEditorViewModel CreateEditor(AgentModelProfile? profile, long? expectedEpoch = null) => new(_catalog, _client, profile, expectedEpoch);
    private async Task ChangeAsync(Func<Task> action)
    {
        IsBusy = true; Error = "";
        try { await action(); }
        catch (Exception error) { Error = Text.Error(error, "AgentSettingsSaveFailed"); }
        finally { IsBusy = false; }
    }
    private void OnChanged(object? sender, EventArgs args)
    {
        if (_context is not null && System.Threading.SynchronizationContext.Current != _context) { _context.Post(_ => Reload(), null); return; }
        Reload();
    }
    private void Reload()
    {
        if (_disposed) return;
        var (models, epoch) = _catalog.Capture(); Rows.Clear();
        foreach (var profile in models.Profiles) Rows.Add(new(profile, models.DefaultProfileId, epoch: epoch));
        if (PendingDelete is { } pending && !models.Profiles.Contains(pending.Profile)) PendingDelete = null;
        Error = _catalog.Error; OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(SyncLabel)); OnPropertyChanged(nameof(IsSynced)); RefreshCommands();
    }
    private void RefreshCommands()
    {
        AddCommand.NotifyCanExecuteChanged(); EditCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
        CancelDeleteCommand.NotifyCanExecuteChanged(); ConfirmDeleteCommand.NotifyCanExecuteChanged(); DefaultCommand.NotifyCanExecuteChanged();
    }
    /// <inheritdoc />
    public void Dispose() { _disposed = true; _catalog.Changed -= OnChanged; EditRequested = null; }
}
