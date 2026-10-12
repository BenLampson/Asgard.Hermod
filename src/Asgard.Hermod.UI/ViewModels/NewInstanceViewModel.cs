namespace Asgard.Hermod.UI.ViewModels;

/// <summary>通过五步向导收集实例配置，连接测试和持久化都由注入服务处理。</summary>
public sealed partial class NewInstanceViewModel : ObservableObject, IDisposable
{
    private readonly IInstanceProviderRegistry _registry;
    private readonly IInstanceConfigurationService _configurations;
    private readonly InstanceSummary? _editingInstance;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _testCancellation;
    private int _configurationRevision;
    private int _verifiedConfigurationRevision = -1;
    private bool _showValidation;
    private bool _disposed;

    /// <summary>由宿主提供顶层类型连接器与配置服务。</summary>
    public NewInstanceViewModel(IInstanceProviderRegistry registry,
        IInstanceConfigurationService configurations)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configurations);
        _registry = registry;
        _configurations = configurations;
        Types = registry.Types.ToArray();
        Steps = [new(1, "选择类型"), new(2, "输入信息"), new(3, "测试连接"),
            new(4, "保存位置"), new(5, "确认创建")];
        NextCommand = new RelayCommand(_ => GoNext(), _ => CanGoNext);
        PreviousCommand = new RelayCommand(_ => SetCurrentStep(CurrentStep - 1), _ => CanGoPrevious);
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
    /// <summary>侧边五步进度。</summary>
    public IReadOnlyList<InstanceWizardStep> Steps { get; }
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
    /// <summary>目前本地是唯一可用保存范围，账户未登录时拒绝切换。</summary>
    public InstanceSaveLocation SaveLocation
    {
        get;
        set
        {
            if (value != InstanceSaveLocation.Local)
                return;
            if (SetProperty(ref field, value))
                RefreshState();
        }
    } = InstanceSaveLocation.Local;
    /// <summary>共享选择卡绑定的本地保存选中状态。</summary>
    public bool IsLocalSave
    {
        get => SaveLocation == InstanceSaveLocation.Local;
        set { if (value) SaveLocation = InstanceSaveLocation.Local; }
    }
    /// <summary>账户连接尚未接入，保持设计中的未登录禁用状态。</summary>
    public bool CanSaveToAccount => false;
    /// <summary>名称缺失时显示就地错误。</summary>
    public string NameError => _showValidation ? ValidateName() : "";
    /// <summary>连接测试的当前结果。</summary>
    public InstanceConnectionTestState TestState { get; private set; }
    /// <summary>经过脱敏处理的连接测试说明。</summary>
    public string TestMessage { get; private set; } = "点击开始测试，验证连接信息和访问权限。";
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
