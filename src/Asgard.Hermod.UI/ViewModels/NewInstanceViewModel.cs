namespace Asgard.Hermod.UI.ViewModels;

/// <summary>收集新实例显示配置；不保存凭据或连接到远程服务。</summary>
public sealed class NewInstanceViewModel : ObservableObject
{
    private string _name = "";
    private string _endpoint = "";
    private string _tag = "Production";
    private InstanceConnectionType _connectionType = InstanceConnectionType.SSH;

    /// <summary>创建表单命令。</summary>
    public NewInstanceViewModel()
    {
        SubmitCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, true), _ => CanCreate);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));
    }

    /// <summary>请求宿主结束表单。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>可选的实例显示类型。</summary>
    public IReadOnlyList<InstanceConnectionType> ConnectionTypes { get; } = Enum.GetValues<InstanceConnectionType>();
    /// <summary>实例名称。</summary>
    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) RefreshValidation(); }
    }
    /// <summary>远程端点地址；本地终端无需填写。</summary>
    public string Endpoint
    {
        get => _endpoint;
        set { if (SetProperty(ref _endpoint, value)) RefreshValidation(); }
    }
    /// <summary>实例分组标签。</summary>
    public string Tag { get => _tag; set => SetProperty(ref _tag, value); }
    /// <summary>选择实例的显示类型。</summary>
    public InstanceConnectionType ConnectionType
    {
        get => _connectionType;
        set
        {
            if (!SetProperty(ref _connectionType, value))
                return;
            OnPropertyChanged(nameof(IsRemote));
            RefreshValidation();
        }
    }
    /// <summary>远程类型需要端点地址。</summary>
    public bool IsRemote => ConnectionType != InstanceConnectionType.Local;
    /// <summary>判断当前表单是否完整。</summary>
    public bool CanCreate => !string.IsNullOrWhiteSpace(Name) && (!IsRemote || !string.IsNullOrWhiteSpace(Endpoint));
    /// <summary>确认创建命令。</summary>
    public RelayCommand SubmitCommand { get; }
    /// <summary>取消表单命令。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>将有效表单变为当前工作区的实例快照。</summary>
    public InstanceSummary CreateInstance()
    {
        if (!CanCreate)
            throw new InvalidOperationException("Complete the instance name and endpoint.");
        string icon = ConnectionType switch
        {
            InstanceConnectionType.Docker => "container",
            InstanceConnectionType.Kubernetes => "hexagon",
            InstanceConnectionType.FTP => "server",
            InstanceConnectionType.Local => "laptop",
            _ => "server",
        };
        return new InstanceSummary(Guid.NewGuid().ToString("N"), Name.Trim(), Name.Trim(), icon,
            string.IsNullOrWhiteSpace(Tag) ? [] : [Tag.Trim()], !IsRemote, Endpoint: IsRemote ? Endpoint.Trim() : "");
    }

    /// <summary>字段变化后刷新创建按钮。</summary>
    private void RefreshValidation()
    {
        OnPropertyChanged(nameof(CanCreate));
        SubmitCommand.NotifyCanExecuteChanged();
    }
}
