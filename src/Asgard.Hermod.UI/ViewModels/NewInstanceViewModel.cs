namespace Asgard.Hermod.UI.ViewModels;

/// <summary>选择类型后通过连接向导收集实例配置，连接测试和持久化都由注入服务处理。</summary>
public sealed partial class NewInstanceViewModel : ObservableObject, IDisposable
{
    private readonly IInstanceProviderRegistry _registry;
    private readonly IInstanceConfigurationService _configurations;
    private readonly IAccountService? _accounts;
    private readonly IConfigurationSyncService? _sync;
    private readonly HermodAccount? _initialAccount;
    private readonly InstanceSummary? _editingInstance;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _testCancellation;
    private int _configurationRevision;
    private int _verifiedConfigurationRevision = -1;
    private bool _showValidation;
    private bool _disposed;

    /// <summary>由宿主提供顶层类型连接器与配置服务。</summary>
    public NewInstanceViewModel(IInstanceProviderRegistry registry,
        IInstanceConfigurationService configurations, IAccountService? accounts = null, IConfigurationSyncService? sync = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configurations);
        _registry = registry;
        _configurations = configurations;
        _accounts = accounts; _sync = sync;
        _initialAccount = accounts?.Account;
        if (accounts is not null) accounts.AccountChanged += OnSaveAccountChanged;
        if (sync is not null) sync.Changed += OnSaveAccountChanged;
        Types = registry.Types.ToArray();
        NextCommand = new RelayCommand(_ => GoNext(), _ => CanGoNext);
        PreviousCommand = new RelayCommand(_ => GoPrevious(), _ => CanGoPrevious);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => !_disposed && !IsSaving);
        ModifyInformationCommand = new RelayCommand(_ => SetCurrentStep(2),
            _ => !_disposed && !IsTesting && !IsSaving && CurrentStep == 3);
        TestConnectionCommand = new AsyncRelayCommand(_ => TestConnectionAsync(), _ => CanTest);
        CancelTestCommand = new RelayCommand(_ => CancelTest(), _ => !_disposed && IsTesting);
        SubmitCommand = new AsyncRelayCommand(_ => SubmitAsync(), _ => CanCreate);
        RefreshState();
    }

    /// <summary>保存成功或用户取消时请求宿主关闭窗口。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>注册表中的顶层实例类型；存储与 Docker 等保持同级。</summary>
    public IReadOnlyList<InstanceTypeDescriptor> Types { get; }
    /// <summary>只展示所选类型下的服务商。</summary>
    public IReadOnlyList<InstanceProviderDescriptor> Providers { get; private set; } = [];
    /// <summary>当前服务商声明的配置字段，每次切换类型或服务重新创建。</summary>
    public IReadOnlyList<InstanceConfigurationFieldViewModel> Fields { get; private set; } = [];
    /// <summary>所选连接流程的进度，类型入口没有步骤集合。</summary>
    public IReadOnlyList<InstanceWizardStep> Steps { get; private set; } = [];
    /// <summary>当前步骤只允许导航命令改变，无法跳过连接测试。</summary>
    public int CurrentStep { get; private set; } = 1;
    /// <summary>先选择顶层类型，再发现对应服务；不把旧类型凭据带到新类型。</summary>
    public InstanceTypeDescriptor? SelectedType
    {
        get;
        set
        {
            if (IsEditing && value != SelectedType)
                return;
            if (value is not null && !Types.Contains(value))
                return;
            if (!SetProperty(ref field, value))
                return;
            Providers = value is null ? [] : _registry.GetProviders(value.Id)
                .Where(provider => provider.Type.Id == value.Id).ToArray();
            OnPropertyChanged(nameof(Providers));
            SelectedProvider = null;
            if (Providers.Count == 1)
                SelectedProvider = Providers[0];
            Name = "";
            CurrentStep = 1;
            RebuildSteps();
            OnPropertyChanged(nameof(CurrentStep));
            InvalidateConnection();
        }
    }
    /// <summary>当前服务商来自注册表，修改后必须重新测试。</summary>
    public InstanceProviderDescriptor? SelectedProvider
    {
        get;
        set
        {
            if (IsEditing && value != SelectedProvider)
                return;
            if (value is not null && !Providers.Contains(value))
                return;
            if (SetProperty(ref field, value))
            {
                ReplaceFields(value);
                InvalidateConnection();
            }
        }
    }
    /// <summary>实例显示名称。</summary>
    public string Name
    {
        get;
        set { if (SetProperty(ref field, value ?? "")) InvalidateConnection(); }
    } = "";
    /// <summary>保存范围由当前登录状态决定，界面没有选择控件。</summary>
    public InstanceSaveLocation SaveLocation => _accounts?.Account is null ? InstanceSaveLocation.Local : InstanceSaveLocation.Account;
    /// <summary>当前保存结果是否属于未登录本地工作区。</summary>
    public bool IsLocalSave => SaveLocation == InstanceSaveLocation.Local;
    /// <summary>当前账户状态只决定提示，不允许手动切换保存范围。</summary>
    public bool CanSaveToAccount => _accounts?.Account is not null;
    /// <summary>当前保存结果名称。</summary>
    public string SaveLocationLabel => Text.Get(CanSaveToAccount ? "SyncCloud" : "Local");
    /// <summary>提示密码尚未设置时的真实状态，不伪称已经上传。</summary>
    public string SaveLocationDescription => Text.Get(!CanSaveToAccount ? "SyncSaveLocalHint"
        : _sync?.HasPassword == true ? "SyncSaveCloudHint" : "SyncSavePasswordHint");
    /// <summary>服务事件切回 UI 上下文更新提示。</summary>
    private void OnSaveAccountChanged(object? sender, EventArgs args)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        { dispatcher.BeginInvoke(() => { if (!_disposed) RefreshState(); }); return; }
        if (!_disposed) RefreshState();
    }
    /// <summary>名称缺失时显示就地错误。</summary>
    public string NameError => _showValidation ? ValidateName() : "";
    /// <summary>连接测试的当前结果。</summary>
    public InstanceConnectionTestState TestState { get; private set; }
    /// <summary>经过脱敏处理的连接测试说明。</summary>
    public string TestMessage { get; private set; } = Text.Get("ClickStartTestToVerifyConnectionInformationAndAccessPermissions");
    /// <summary>经过脱敏处理的服务商错误码。</summary>
    public string TestErrorCode { get; private set; } = "";
    /// <summary>连接器提供的真实检查项目。</summary>
    public IReadOnlyList<InstanceConnectionCheck> TestChecks { get; private set; } = [];
    /// <summary>测试未结束期间禁止重复测试或推进。</summary>
    public bool IsTesting { get; private set; }
    /// <summary>创建期间禁止重复提交和修改表单。</summary>
    public bool IsSaving { get; private set; }
    /// <summary>保存失败保留填写内容，并仅显示安全提示。</summary>
    public string SaveError { get; private set; } = "";
    /// <summary>持久化成功后返回的安全导航快照。</summary>
    public InstanceSummary? CreatedInstance { get; private set; }
    /// <summary>推进步骤时验证当前步骤要求。</summary>
    public RelayCommand NextCommand { get; }
    /// <summary>返回前一步，不重置已经填写的内容。</summary>
    public RelayCommand PreviousCommand { get; }
    /// <summary>关闭向导并取消进行中的连接测试。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>失败后返回填写页修正凭据。</summary>
    public RelayCommand ModifyInformationCommand { get; }
    /// <summary>调用选中服务商声明的真实连接测试。</summary>
    public AsyncRelayCommand TestConnectionCommand { get; }
    /// <summary>取消正在进行的连接测试。</summary>
    public RelayCommand CancelTestCommand { get; }
    /// <summary>调用配置服务提交已经验证的连接。</summary>
    public AsyncRelayCommand SubmitCommand { get; }

    /// <summary>关闭窗口后取消异步工作，所有命令都失效。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_accounts is not null) _accounts.AccountChanged -= OnSaveAccountChanged;
        if (_sync is not null) _sync.Changed -= OnSaveAccountChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        foreach (var field in Fields)
            field.PropertyChanged -= OnFieldPropertyChanged;
        RefreshState();
    }

    /// <summary>字段对象按服务隔离，离开的服务会清除全部填写值并释放事件订阅。</summary>
    private void ReplaceFields(InstanceProviderDescriptor? provider)
    {
        foreach (var field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
            field.Value = "";
        }
        Fields = provider?.Fields.Select(definition => new InstanceConfigurationFieldViewModel(definition)).ToArray() ?? [];
        foreach (var field in Fields)
            field.PropertyChanged += OnFieldPropertyChanged;
        _showValidation = false;
        OnPropertyChanged(nameof(Fields));
    }

    /// <summary>任意配置字段变更都作废旧连接测试，不依赖具体认证字段名称。</summary>
    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(InstanceConfigurationFieldViewModel.Value))
            InvalidateConnection();
    }
}
