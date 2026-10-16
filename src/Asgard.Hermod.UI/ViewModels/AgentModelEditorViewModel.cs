using System.Diagnostics;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>隐藏已保存密钥的编辑草稿；服务地址变化时绝不自动传递旧密钥。</summary>
public sealed class AgentModelEditorViewModel : ObservableObject, IDisposable
{
    private readonly AgentModelCatalog _catalog;
    private readonly IAgentClient _client;
    private readonly long _epoch;
    private readonly System.Threading.SynchronizationContext? _context = System.Threading.SynchronizationContext.Current;
    private AgentModelProfile? _original;
    private CancellationTokenSource? _testing;
    private long _revision;
    private bool _disposed;
    /// <summary>捕获原账号和记录版本，密钥不回填到控件。</summary>
    public AgentModelEditorViewModel(AgentModelCatalog catalog, IAgentClient client, AgentModelProfile? original, long? expectedEpoch = null)
    {
        _catalog = catalog; _client = client; _original = original; _epoch = expectedEpoch ?? catalog.Epoch;
        Name = original?.Name ?? ""; Endpoint = original?.Endpoint ?? ""; Model = original?.Model ?? "";
        IsDefault = original is not null && original.Id == catalog.Current.DefaultProfileId;
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !_disposed && !IsSaving && !IsTesting);
        TestCommand = new AsyncRelayCommand(_ => TestAsync(), _ => !_disposed && !IsSaving && !IsTesting);
        ClearKeyCommand = new AsyncRelayCommand(_ => ClearKeyAsync(), _ => !_disposed && !IsSaving && _original?.ApiKey.Length > 0);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false), _ => !IsSaving);
        catalog.Changed += OnCatalogChanged;
    }
    /// <summary>成功提交或取消后由宿主关闭。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>编辑或新增标题。</summary>
    public string Title => Text.Get(_original is null ? "AgentModelAdd" : "AgentModelEdit");
    /// <summary>用户定义名称。</summary>
    public string Name { get; set { if (SetProperty(ref field, value)) InvalidateTest(); } } = "";
    /// <summary>HTTPS 或回环 HTTP 地址。</summary>
    public string Endpoint { get; set { if (SetProperty(ref field, value)) { InvalidateTest(); OnPropertyChanged(nameof(KeyHint)); OnPropertyChanged(nameof(HasRetainedKey)); } } } = "";
    /// <summary>模型标识原样发送。</summary>
    public string Model { get; set { if (SetProperty(ref field, value)) InvalidateTest(); } } = "";
    /// <summary>只含这次明确输入的新密钥，关闭后清除。</summary>
    public string ApiKey { get; set { if (SetProperty(ref field, value)) { InvalidateTest(); OnPropertyChanged(nameof(HasRetainedKey)); } } } = "";
    /// <summary>是否用于之后创建的新会话。</summary>
    public bool IsDefault { get; set { if (SetProperty(ref field, value)) InvalidateTest(); } }
    /// <summary>提交中防止重复保存。</summary>
    public bool IsSaving { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>真实网络测试进行中。</summary>
    public bool IsTesting { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>连接测试的真实耗时或安全错误。</summary>
    public string TestResult { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>实际请求成功后才显示成功图标和颜色。</summary>
    public bool TestSucceeded { get; private set => SetProperty(ref field, value); }
    /// <summary>安全保存结果。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>说明留空、地址变化和清除的真实行为。</summary>
    public string KeyHint => Text.Get(_original?.ApiKey.Length > 0 ? Endpoint.Trim() == _original.Endpoint ? "AgentModelKeyRetained" : "AgentModelKeyEndpointChanged" : "AgentKeyNotSet");
    /// <summary>固定长度占位仅表示已有密钥，不回填密钥或泄露其长度。</summary>
    public bool HasRetainedKey => ApiKey.Length == 0 && _original?.ApiKey.Length > 0 && Endpoint.Trim() == _original.Endpoint;
    /// <summary>验证后保存设备密文并请求账号同步。</summary>
    public AsyncRelayCommand SaveCommand { get; }
    /// <summary>仅显式点击时发送最小测试请求。</summary>
    public AsyncRelayCommand TestCommand { get; }
    /// <summary>明确清除立即保存并传播；取消编辑不会恢复旧密钥。</summary>
    public AsyncRelayCommand ClearKeyCommand { get; }
    /// <summary>取消草稿。</summary>
    public RelayCommand CancelCommand { get; }
    private AgentModelProfile Draft()
    {
        string endpoint = Endpoint.Trim();
        string key = ApiKey.Length > 0 ? ApiKey : endpoint == _original?.Endpoint ? _original.ApiKey : "";
        var profile = new AgentModelProfile(_original?.Id ?? Guid.NewGuid().ToString("N"), Name.Trim(), endpoint, Model.Trim(), key, DateTimeOffset.UtcNow);
        AgentModelValidation.Validate(new([profile])); return profile;
    }
    private async Task SaveAsync()
    {
        IsSaving = true; Error = "";
        try
        {
            var updated = Draft(); await _catalog.SaveAsync(_original, updated, IsDefault, _epoch);
            ApiKey = ""; _original = updated; CloseRequested?.Invoke(this, true);
        }
        catch (Exception error) { Error = Text.Error(error, "AgentSettingsSaveFailed"); }
        finally { IsSaving = false; }
    }
    private async Task ClearKeyAsync()
    {
        if (_original is not { } original) return;
        InvalidateTest(); IsSaving = true; Error = "";
        try
        {
            var cleared = original with { ApiKey = "", UpdatedAt = DateTimeOffset.UtcNow };
            await _catalog.SaveAsync(original, cleared, _catalog.Current.DefaultProfileId == original.Id, _epoch);
            _original = cleared; ApiKey = ""; OnPropertyChanged(nameof(KeyHint)); OnPropertyChanged(nameof(HasRetainedKey));
        }
        catch (Exception error) { Error = Text.Error(error, "AgentSettingsSaveFailed"); }
        finally { IsSaving = false; }
    }
    private async Task TestAsync()
    {
        using var pending = new CancellationTokenSource(TimeSpan.FromMinutes(2)); _testing = pending;
        long revision = _revision; IsTesting = true; TestResult = ""; TestSucceeded = false;
        try
        {
            if (_epoch != _catalog.Epoch || !_catalog.IsReady) throw Text.Exception("AgentModelOwnerChanged");
            if (_original is not null && !_catalog.Current.Profiles.Contains(_original)) throw Text.Exception("AgentModelChanged");
            var draft = Draft(); var stopwatch = Stopwatch.StartNew();
            await _client.ForOptions(new(draft.Endpoint, draft.Model, draft.ApiKey), draft.Name)
                .CompleteAsync([new(AgentRole.User, "Reply with OK.")], pending.Token).WaitAsync(pending.Token);
            if (!_disposed && revision == _revision && _epoch == _catalog.Epoch)
            {
                TestResult = Text.Format("AgentModelTestSucceeded", stopwatch.ElapsedMilliseconds);
                TestSucceeded = true;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed && revision == _revision) TestResult = Text.Error(error, "AgentRequestFailed"); }
        finally { _testing = null; IsTesting = false; }
    }
    private void InvalidateTest() { _revision++; _testing?.Cancel(); TestResult = ""; TestSucceeded = false; }
    private void OnCatalogChanged(object? sender, EventArgs args)
    {
        if (_context is not null && System.Threading.SynchronizationContext.Current != _context) { _context.Post(_ => ApplyCatalogChange(), null); return; }
        ApplyCatalogChange();
    }
    private void ApplyCatalogChange()
    {
        if (_disposed) return;
        if (_epoch != _catalog.Epoch || _original is not null && !_catalog.Current.Profiles.Contains(_original)) InvalidateTest();
    }
    private void Refresh() { SaveCommand?.NotifyCanExecuteChanged(); TestCommand?.NotifyCanExecuteChanged(); ClearKeyCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); }
    /// <inheritdoc />
    public void Dispose() { _disposed = true; _catalog.Changed -= OnCatalogChanged; _testing?.Cancel(); ApiKey = ""; CloseRequested = null; }
}
