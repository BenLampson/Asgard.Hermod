using System.Text.Json;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Abstractions.Services;

namespace Asgard.Hermod.Agent;

/// <summary>当前账号的模型目录；写入与账号切换、同步恢复使用同一个串行边界。</summary>
public sealed class AgentModelCatalog : IDisposable
{
    private readonly IAgentModelStore _store;
    private readonly IAgentPreferenceStore _legacy;
    private readonly AgentConfiguration _configuration;
    private readonly IConfigurationSyncService _sync;
    private readonly IConfigurationSyncStateStore _states;
    private readonly IAccountService _accounts;
    private readonly object _gate = new();
    private AgentModelSet _current = AgentModelSet.Empty;
    private string _identity = "", _owner = "", _error = "";
    private long _epoch;
    private bool _initialized, _disposed;

    /// <summary>只订阅配置变化，初始化在语言和 SQLite 迁移完成后显式调用。</summary>
    public AgentModelCatalog(IAgentModelStore store, IAgentPreferenceStore legacy, AgentConfiguration configuration,
        IConfigurationSyncService sync, IConfigurationSyncStateStore states, IAccountService accounts)
    {
        _store = store; _legacy = legacy; _configuration = configuration; _sync = sync; _states = states; _accounts = accounts;
        store.Changed += OnChanged; sync.Changed += OnChanged; accounts.AccountChanged += OnChanged;
    }

    /// <summary>目录和权限变化通知；订阅者自行切回界面线程。</summary>
    public event EventHandler? Changed;
    /// <summary>不可变配置快照，不可写日志。</summary>
    public AgentModelSet Current { get { lock (_gate) return IsReady ? _current : AgentModelSet.Empty; } }
    /// <summary>列表行同时捕获配置与账号版本，避免后台切换期间拼接两个快照。</summary>
    public (AgentModelSet Models, long Epoch) Capture() { lock (_gate) return (IsReady ? _current : AgentModelSet.Empty, _epoch); }
    /// <summary>账号切换完成之前不向新 Session 暴露上个账号的配置。</summary>
    public bool IsReady => !_disposed && _sync.IsWorkspacePrepared && _owner == _states.ActiveOwner && _identity == AccountIdentity();
    /// <summary>账号身份或工作区归属变更后递增，旧 Session 不会重定向到新账号。</summary>
    public long Epoch { get { lock (_gate) return _epoch; } }
    /// <summary>安全的解密或目录读取错误。</summary>
    public string Error { get { lock (_gate) return IsReady ? _error : Text.Get("AgentModelOwnerChanged"); } }
    /// <summary>当前账号的真实同步状态。</summary>
    public ConfigurationSyncStatus SyncStatus => _sync.Status;

    /// <summary>首次把旧的地址和模型迁入设备密文；迁移失败保留原始数据。</summary>
    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            _initialized = true;
        }
        try
        {
            if (_store.Load() is null)
            {
                var old = _legacy.Load(); AgentModelSet migrated = AgentModelSet.Empty;
                if (old.Endpoint.Length > 0 && old.Model.Length > 0)
                {
                    var profile = new AgentModelProfile(Guid.NewGuid().ToString("N"), Text.Get("AgentMigratedProfile"), old.Endpoint, old.Model, "", DateTimeOffset.UtcNow);
                    migrated = new([profile], profile.Id);
                }
                _store.Save(migrated);
            }
        }
        catch (Exception error) { lock (_gate) _error = Text.Error(error, "AgentModelUnlockFailed"); }
        Reload();
    }

    /// <summary>创建当前默认配置的独立 Session；后续改默认不会切换既有 Session。</summary>
    public AgentModelSession CreateSession(IAgentClient client) => new(this, client);

    /// <summary>保存时比对原配置和账号版本，防止旧编辑器覆盖同步更新或复活删除项。</summary>
    public Task SaveAsync(AgentModelProfile? original, AgentModelProfile updated, bool isDefault, long expectedEpoch, CancellationToken token = default)
        => ChangeAsync(expectedEpoch, current =>
        {
            var existing = current.Profiles.FirstOrDefault(profile => profile.Id == updated.Id);
            if (existing != original || original is null && existing is not null) throw Text.Exception("AgentModelChanged");
            var profiles = existing is null ? current.Profiles.Append(updated).ToArray()
                : current.Profiles.Select(profile => profile.Id == updated.Id ? updated : profile).ToArray();
            string selected = isDefault ? updated.Id : current.DefaultProfileId == updated.Id ? "" : current.DefaultProfileId;
            return new(profiles, selected);
        }, token);

    /// <summary>删除立即保存并触发加密同步；未选中的默认引用保持原样。</summary>
    public Task DeleteAsync(AgentModelProfile original, long expectedEpoch, CancellationToken token = default)
        => ChangeAsync(expectedEpoch, current =>
        {
            if (current.Profiles.FirstOrDefault(profile => profile.Id == original.Id) != original) throw Text.Exception("AgentModelChanged");
            return new(current.Profiles.Where(profile => profile.Id != original.Id).ToArray(), current.DefaultProfileId == original.Id ? "" : current.DefaultProfileId);
        }, token);

    /// <summary>默认值仅影响之后创建的 Session。</summary>
    public Task SetDefaultAsync(string id, long expectedEpoch, CancellationToken token = default)
        => ChangeAsync(expectedEpoch, current => current with { DefaultProfileId = id }, token);
    /// <summary>用户列表入口同时校验行版本，不把旧账号或旧列表行解释为当前配置。</summary>
    public Task SetDefaultAsync(AgentModelProfile original, long expectedEpoch, CancellationToken token = default)
        => ChangeAsync(expectedEpoch, current =>
        {
            if (current.Profiles.FirstOrDefault(profile => profile.Id == original.Id) != original) throw Text.Exception("AgentModelChanged");
            return current with { DefaultProfileId = original.Id };
        }, token);

    private async Task ChangeAsync(long expectedEpoch, Func<AgentModelSet, AgentModelSet> change, CancellationToken token)
    {
        await _sync.RunLocalChangeAsync(() =>
        {
            Reload();
            AgentModelSet updated;
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || expectedEpoch != _epoch) throw Text.Exception("AgentModelOwnerChanged");
                if (_error.Length > 0) throw Text.Exception("AgentModelUnlockFailed");
                updated = change(_current); AgentModelValidation.Validate(updated);
            }
            // 不在目录锁内提交或发通知，避免后台恢复与界面读取 Session 形成锁顺序反转。
            _store.Save(updated);
            return Task.CompletedTask;
        }, token).ConfigureAwait(false);
        _sync.RequestSync();
    }

    private void OnChanged(object? sender, EventArgs args) { if (_initialized && !_disposed) Reload(); }
    private void Reload()
    {
        lock (_gate)
        {
            string identity = AccountIdentity();
            string owner = _states.ActiveOwner;
            if (_identity != identity || _owner != owner) { _identity = identity; _owner = owner; _epoch++; }
            try { _current = (_store.Load() ?? AgentModelSet.Empty).Freeze(); _error = ""; }
            catch (Exception error) { _current = AgentModelSet.Empty; _error = Text.Error(error, "AgentModelUnlockFailed"); }
            var selected = IsReady ? _current.Profiles.FirstOrDefault(profile => profile.Id == _current.DefaultProfileId) : null;
            _configuration.Update(selected?.Endpoint ?? "", selected?.Model ?? "", selected?.ApiKey ?? "");
        }
        var handlers = Changed;
        if (handlers is not null) foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { /* 界面已经关闭不能影响已提交的配置。 */ }
        }
    }
    private string AccountIdentity()
    {
        var account = _accounts.Account;
        return account is null ? "" : JsonSerializer.Serialize(new[] { account.ApplicationId, account.TenantId, account.SubjectId });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true; _store.Changed -= OnChanged; _sync.Changed -= OnChanged; _accounts.AccountChanged -= OnChanged;
    }
}
