using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Asgard.Hermod.Identity;

/// <summary>同步与账户工作区切换串行；离线修改保留，只有无歧义的合并才提交。</summary>
public sealed partial class ConfigurationSyncService : BackgroundService, IConfigurationSyncService
{
    private readonly IAccountService _accounts;
    private readonly ILocalWorkspaceStore _local;
    private readonly IConfigurationSyncStateStore _store;
    private readonly HermodIdentityOptions _options;
    private readonly ConfigurationTransport _transport;
    private readonly ConfigurationCodec _codec;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private string? _conflictRevision;
    private WorkspaceSnapshot? _conflictLocal;
    private bool _started;

    /// <summary>只依赖共享合同，后台服务不引用 WPF 或数据库实现。</summary>
    public ConfigurationSyncService(IAccountService accounts, ILocalWorkspaceStore local, IConfigurationSyncStateStore store,
        IHttpClientFactory clients, HermodIdentityOptions options, IInstanceProviderRegistry registry)
    {
        _accounts = accounts; _local = local; _store = store; _options = options;
        _transport = new(clients); _codec = new(registry);
        accounts.AccountChanged += OnAccountChanged;
    }
    /// <inheritdoc />
    public ConfigurationSyncStatus Status { get; private set; } = ConfigurationSyncStatus.Local;
    /// <inheritdoc />
    public string Message { get; private set; } = "";
    /// <inheritdoc />
    public DateTime? LastSyncedAt { get; private set; }
    /// <inheritdoc />
    public bool HasPassword { get; private set; }
    /// <inheritdoc />
    public IReadOnlyList<ConfigurationConflict> Conflicts { get; private set; } = [];
    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;
        _started = true;
        await PrepareWorkspaceAsync(cancellationToken);
        await base.StartAsync(cancellationToken); RequestSync();
    }
    /// <summary>队列合并多次编辑，安静轮询检查其他设备更新。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                wait.CancelAfter(TimeSpan.FromSeconds(30));
                try { await _requests.Reader.ReadAsync(wait.Token); await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
                while (_requests.Reader.TryRead(out _)) { }
                await SynchronizeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { SetStatus(ConfigurationSyncStatus.Failed, Text.Error(error, "SyncAutoFailed")); }
        }
    }
    /// <inheritdoc />
    public void RequestSync()
    {
        _requests.Writer.TryWrite(true);
        if (_accounts.Account is not null && Status != ConfigurationSyncStatus.Synchronizing && Status != ConfigurationSyncStatus.Conflict)
            SetStatus(HasPassword ? ConfigurationSyncStatus.Pending : ConfigurationSyncStatus.PasswordRequired);
    }
    /// <summary>事件不等待会话锁，异步队列在身份提交完成后接管。</summary>
    private void OnAccountChanged(object? sender, EventArgs args) => RequestSync();

    /// <inheritdoc />
    public Task PrepareWorkspaceAsync(CancellationToken cancellationToken = default) => RunAsync(async (account, _) =>
        await SwitchWorkspaceAsync(account, cancellationToken), cancellationToken, false);

    /// <inheritdoc />
    public Task RunLocalChangeAsync(Func<Task> action, CancellationToken cancellationToken = default) => RunAsync(async (account, _) =>
    {
        await SwitchWorkspaceAsync(account, cancellationToken);
        var state = _store.Load(account is null ? "" : Owner(account));
        await action();
        await RefreshCloudInstanceStateAsync(account, state.Baseline, cancellationToken);
        RequestSync();
    }, cancellationToken, false);

    /// <summary>登录前配置保留为本地副本；不同账户绝不共享当前工作区。</summary>
    private async Task SwitchWorkspaceAsync(HermodAccount? account, CancellationToken cancellationToken)
    {
        string owner = account is null ? "" : Owner(account);
        if (_store.ActiveOwner != owner)
        {
            var current = await _local.ExportAsync(cancellationToken);
            string previous = _store.ActiveOwner;
            _store.Save(previous, _store.Load(previous) with { WorkingCopy = current });
            var next = _store.Load(owner);
            var replacement = next.WorkingCopy ?? next.Baseline ?? (previous.Length == 0 && owner.Length != 0
                ? current : new WorkspaceSnapshot(1, [], current.Tags.Where(tag => tag.IsBuiltIn).ToArray()));
            await _local.SwitchOwnerAsync(owner, replacement, current, cancellationToken);
        }
        var state = _store.Load(owner);
        HasPassword = account is not null && state.Password is not null; LastSyncedAt = state.LastSyncedAt;
        await RefreshCloudInstanceStateAsync(account, state.Baseline, cancellationToken);
        if (account is null) { Conflicts = []; SetStatus(ConfigurationSyncStatus.Local); }
        else if (!HasPassword) { Conflicts = []; SetStatus(ConfigurationSyncStatus.PasswordRequired); }
    }

    /// <inheritdoc />
    public Task EnableAsync(string password, CancellationToken cancellationToken = default) => RunAsync(async (account, token) =>
    {
        if (account is null) throw Text.Exception("SignInAgain");
        WorkspaceEncryption.ValidatePassword(password);
        await SwitchWorkspaceAsync(account, cancellationToken);
        var cloud = await _transport.ReadAsync(token, cancellationToken);
        string owner = Owner(account);
        // 保存前验证旧密码；云端为空时允许首次设置。
        await Task.Run(() => _codec.Decode(cloud, password, owner), cancellationToken);
        _store.Save(owner, _store.Load(owner) with { Password = password }); HasPassword = true;
        SetStatus(ConfigurationSyncStatus.Pending); RequestSync();
    }, cancellationToken);

    /// <inheritdoc />
    public Task ForgetPasswordAsync(CancellationToken cancellationToken = default) => RunAsync((account, _) =>
    {
        if (account is null) return Task.CompletedTask;
        string owner = Owner(account); _store.Save(owner, _store.Load(owner) with { Password = null });
        HasPassword = false; Conflicts = []; SetStatus(ConfigurationSyncStatus.PasswordRequired); return Task.CompletedTask;
    }, cancellationToken, false);

    /// <inheritdoc />
    public Task SynchronizeAsync(CancellationToken cancellationToken = default) => SynchronizeCoreAsync(null, cancellationToken);
    /// <inheritdoc />
    public Task ResolveAsync(IReadOnlyDictionary<string, bool> useCloud, CancellationToken cancellationToken = default) => SynchronizeCoreAsync(useCloud, cancellationToken);

    /// <summary>云端和本机都保持当前冲突版本才接受选择；先完整验证再上传。</summary>
    private Task SynchronizeCoreAsync(IReadOnlyDictionary<string, bool>? choices, CancellationToken cancellationToken) => RunAsync(async (account, token) =>
    {
        await SwitchWorkspaceAsync(account, cancellationToken);
        if (account is null || !HasPassword) return;
        string owner = Owner(account); var state = _store.Load(owner);
        SetStatus(ConfigurationSyncStatus.Synchronizing);
        try
        {
            var local = await _local.ExportAsync(cancellationToken);
            _store.Save(owner, state with { WorkingCopy = local });
            var cloud = await _transport.ReadAsync(token, cancellationToken);
            var remote = await Task.Run(() => _codec.Decode(cloud, state.Password!, owner), cancellationToken);
            if (cloud.Protection is null && cloud.LegacyWorkspace is null)
                remote = remote with { Tags = local.Tags.Where(tag => tag.IsBuiltIn).ToArray() };
            _local.Validate(remote);
            UpdateCloudInstanceState(account, local, remote);
            if (choices is not null && (cloud.Revision != _conflictRevision || !WorkspaceContent.Equal(local, _conflictLocal))) choices = null;
            var merge = new WorkspaceMerge(); var candidate = merge.Merge(state.Baseline, local, remote, choices);
            Conflicts = merge.Conflicts.ToArray();
            if (Conflicts.Count == 0)
            {
                try { _local.Validate(candidate); }
                catch (Exception)
                {
                    // 删除关联、同名标签或合并后循环不能静默丢弃数据，交给完整候选选择。
                    const string structure = "workspace:structure";
                    if (choices?.TryGetValue(structure, out bool useCloud) == true) candidate = useCloud ? remote : local;
                    else Conflicts = [new(structure, Text.Get("AccountAndSync"), "SyncConflictStructure", Text.Get("Local"), Text.Get("SyncCloud"))];
                }
            }
            if (Conflicts.Count > 0)
            {
                _conflictRevision = cloud.Revision; _conflictLocal = local;
                SetStatus(ConfigurationSyncStatus.Conflict); return;
            }
            _local.Validate(candidate);
            if (!WorkspaceContent.Equal(candidate, remote) || cloud.Protection is null)
            {
                var encoded = await Task.Run(() => _codec.Encode(candidate, remote, cloud, state.Password!, owner), cancellationToken);
                cloud = await _transport.WriteAsync(token, new(cloud.Revision, encoded), cancellationToken);
                candidate = await Task.Run(() => _codec.Decode(cloud, state.Password!, owner), cancellationToken);
            }
            // 应用失败保留旧共同基准，下次仍能区分云端新增与网络期间的本机编辑。
            UpdateCloudInstanceState(account, local, candidate);
            if (!WorkspaceContent.Equal(local, candidate)) await _local.RestoreIfUnchangedAsync(candidate, local, cancellationToken);
            UpdateCloudInstanceState(account, candidate, candidate);
            _store.Save(owner, state with { Baseline = candidate, WorkingCopy = candidate, Revision = cloud.Revision, LastSyncedAt = DateTime.UtcNow });
            LastSyncedAt = DateTime.UtcNow; Conflicts = []; _conflictLocal = null; _conflictRevision = null;
            SetStatus(ConfigurationSyncStatus.Synced);
        }
        catch (OperationCanceledException) { SetStatus(ConfigurationSyncStatus.Pending); throw; }
        catch (Exception error) { SetStatus(ConfigurationSyncStatus.Failed, Text.Error(error, "SyncAutoFailed")); throw; }
    }, cancellationToken);

    /// <summary>同步锁先于身份锁；界面本机保存也遵守相同顺序。</summary>
    private async Task RunAsync(Func<HermodAccount?, string, Task> action, CancellationToken cancellationToken, bool authenticated = true)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!authenticated) await _accounts.RunWithAccountAsync(account => action(account, ""), cancellationToken);
            else if (_accounts.Account is null) await action(null, "");
            else await _accounts.RunAuthenticatedAsync((account, token) => action(account, token), cancellationToken);
        }
        finally { _gate.Release(); }
    }
    /// <summary>密码和同步状态按发行者、应用、租户与主体隔离。</summary>
    private string Owner(HermodAccount account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { _options.Authority, _options.ApplicationId, account.TenantId, account.SubjectId }))));
    /// <summary>通知 UI 时只传安全文案，控件自行切回 Dispatcher。</summary>
    private void SetStatus(ConfigurationSyncStatus status, string? message = null)
    {
        Status = status; Message = message ?? Text.Get("SyncStatus" + status);
        EventHandler? handlers = Changed;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { /* 窗口关闭或刷新失败不能把已提交的配置误报成保存失败。 */ }
        }
    }
    /// <inheritdoc />
    public override void Dispose()
    {
        _accounts.AccountChanged -= OnAccountChanged; base.Dispose();
    }
}
