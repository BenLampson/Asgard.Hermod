using Asgard.Hermod.Abstractions.Models;

namespace Asgard.Hermod.Agent;

/// <summary>来源 Session 的模型选择；默认值只在创建时读取，任务中冻结整个连接。</summary>
public sealed class AgentModelSession : IDisposable
{
    private readonly AgentModelCatalog _catalog;
    private readonly IAgentClient _client;
    private readonly object _gate = new();
    private readonly long _epoch;
    private AgentModelProfile? _selected;
    private CancellationTokenSource? _active;
    private IAgentClient? _activeClient;
    private TaskCompletionSource? _idle;
    private bool _disposed;

    internal AgentModelSession(AgentModelCatalog catalog, IAgentClient client)
    {
        _catalog = catalog; _client = client; _epoch = catalog.Epoch;
        _selected = catalog.Current.Profiles.FirstOrDefault(profile => profile.Id == catalog.Current.DefaultProfileId);
        catalog.Changed += OnChanged;
    }
    /// <summary>配置或忙碌变化；控件自行切回其 Dispatcher。</summary>
    public event EventHandler? Changed;
    /// <summary>当前账号可选配置；旧账号来源没有新账号列表。</summary>
    public IReadOnlyList<AgentModelProfile> Profiles => IsOwnerCurrent ? _catalog.Current.Profiles : [];
    /// <summary>只公开稳定选择身份，不显示密钥。</summary>
    public string SelectedId { get { lock (_gate) return _selected?.Id ?? ""; } }
    /// <summary>选择标签用于工具栏，密钥不进入控件。</summary>
    public string Label { get { lock (_gate) return _selected is null ? Text.Get("ChatConfigureModel") : Text.Format("AgentModelIdentity", _selected.Name, _selected.Model); } }
    /// <summary>配置名称只用于显示。</summary>
    public string Name { get { lock (_gate) return _selected?.Name ?? Text.Get("ChatConfigureModel"); } }
    /// <summary>实际选定的模型标识。</summary>
    public string Model { get { lock (_gate) return _selected?.Model ?? ""; } }
    /// <summary>是否仍属于当前工作区账号。</summary>
    public bool IsOwnerCurrent => !_disposed && _catalog.IsReady && _epoch == _catalog.Epoch;
    /// <summary>已删除或无法解密的配置不会自动跳到其他服务。</summary>
    public bool IsConfigured { get { lock (_gate) return IsOwnerCurrent && _selected is not null && _catalog.Error.Length == 0 && _catalog.Current.Profiles.Contains(_selected); } }
    /// <summary>任务仍在进行，需要明确停止后才能切换。</summary>
    public bool IsBusy { get { lock (_gate) return _active is not null; } }

    /// <summary>开始任务时捕获完整配置；手动执行旧建议可不要求模型，但仍校验账号。</summary>
    public AgentModelTask BeginTask(CancellationToken token, bool requireModel = true)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (!IsOwnerCurrent) throw Text.Exception("AgentModelOwnerChanged");
            // 界面从上下文读取开始持有任务，协议对话只允许借用同一个取消范围。
            if (_active is not null && token == _active.Token) return new(_activeClient!, _active, () => { }, ownsSource: false);
            if (_active is not null) throw Text.Exception("AgentBusy");
            if (requireModel && !IsConfigured) throw Text.Exception("AgentConfigurationRequired");
            var options = _selected is null ? new AgentModelOptions() : new(_selected.Endpoint, _selected.Model, _selected.ApiKey);
            var frozen = _client.ForOptions(options, _selected?.Name ?? "");
            var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            _active = source; _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeClient = frozen;
            return new(_activeClient, source, () => Release(source));
        }
    }

    /// <summary>忙碌时拒绝直接切换；调用方必须呈现明确的停止并切换动作。</summary>
    public void Select(string id)
    {
        lock (_gate)
        {
            if (!IsOwnerCurrent) throw Text.Exception("AgentModelOwnerChanged");
            if (_active is not null) throw Text.Exception("AgentModelStopBeforeSwitch");
            _selected = _catalog.Current.Profiles.FirstOrDefault(profile => profile.Id == id) ?? throw Text.Exception("AgentModelChanged");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>显式停止任务并等待退出，之后才应用用户选定的配置，不重发历史。</summary>
    public async Task StopAndSelectAsync(string id, CancellationToken token = default)
    {
        Task idle;
        lock (_gate) { _active?.Cancel(); idle = _idle?.Task ?? Task.CompletedTask; }
        await idle.WaitAsync(token).ConfigureAwait(false); Select(id);
    }

    private void Release(CancellationTokenSource source)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(source, _active)) return;
            _active = null; _activeClient = null; _idle?.TrySetResult(); _idle = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void OnChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            var next = IsOwnerCurrent ? _catalog.Current.Profiles.FirstOrDefault(profile => profile.Id == _selected?.Id) : null;
            if (next != _selected || !IsOwnerCurrent || _catalog.Error.Length > 0) _active?.Cancel();
            _selected = next;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _active?.Cancel(); }
        _catalog.Changed -= OnChanged; Changed?.Invoke(this, EventArgs.Empty);
    }
}
