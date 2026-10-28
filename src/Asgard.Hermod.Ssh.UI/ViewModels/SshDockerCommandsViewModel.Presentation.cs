namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>显示状态全部从稳定操作 ID 和共享资源推导。</summary>
public sealed partial class SshDockerCommandsViewModel
{
    /// <summary>当前选择的操作标题。</summary>
    public string OperationTitle => SelectedOperation?.Title ?? Text.Get("DockerLibrary");
    /// <summary>资源选择后才显示动作，空列表有明确提示。</summary>
    public bool HasContainer => SelectedContainer is not null;
    /// <summary>容器消失或筛选隐藏时，主内容提示重新选择目标。</summary>
    public bool HasNoContainer => !HasContainer && (IsResourceTool || IsDetailsTool);
    /// <summary>成功读取后，筛选为空才显示空状态。</summary>
    public bool HasNoContainers => IsInitialized && ContainerView.IsEmpty && !IsBusy;
    /// <summary>筛选无结果与服务器没有容器使用不同提示。</summary>
    public string ContainerEmptyMessage => Text.Get(ContainerSearch.Length > 0 || ContainerState.Length > 0 ? "SshMonitorNoMatches" : "DockerNoContainers");
    /// <summary>仅次级工具显示独立的操作提交按钮。</summary>
    public bool IsOtherOperation => SelectedOperation?.Id is "images" or "pull" || NeedsCompose;
    /// <summary>动作结果使用执行时的名称，不随列表选择变化。</summary>
    public string ResultTarget { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>实际 SSH 目标。</summary>
    public string Host => _host();
    /// <summary>表单在执行与确认期间禁止编辑。</summary>
    public bool CanEdit => !IsBusy;
    /// <summary>无有效能力与参数时不允许执行。</summary>
    public bool CanExecute => !_disposed && !IsBusy && _plan is not null && _connection() is not null;
    /// <summary>插入还需要当前控制台可输入。</summary>
    public bool CanInsert => CanExecute && _canInsert();
    /// <summary>容器操作的字段显隐。</summary>
    public bool NeedsContainer => SelectedOperation?.Definition.RequiresContainer == true;
    /// <summary>日志专用选项。</summary>
    public bool IsLogs => SelectedOperation?.Id is "logs" or "compose-logs";
    /// <summary>停止和重启提供等待时长。</summary>
    public bool NeedsTimeout => SelectedOperation?.Id is "stop" or "restart";
    /// <summary>拉取镜像的引用字段。</summary>
    public bool NeedsImage => SelectedOperation?.Id == "pull";
    /// <summary>Compose 操作需要显式目录、文件和可选服务。</summary>
    public bool NeedsCompose => SelectedOperation?.Definition.RequiresCompose == true;
    /// <summary>普通变更的影响提示。</summary>
    public bool ChangesState => SelectedOperation?.Definition.ChangesState == true;
    /// <summary>预览内容由构建器生成，不能编辑为任意 shell。</summary>
    public string Preview => _plan?.Command ?? "";
    /// <summary>结构化环境版本摘要。</summary>
    public string EnvironmentSummary => Environment is { Installed: true } e ? Text.Format("DockerEnvironmentSummary", e.CliVersion, e.EngineVersion,
        e.ComposeVersion.Length == 0 ? Text.Get("DockerComposeUnavailable") : e.ComposeVersion) : Text.Get("DockerDetectFirst");
    /// <summary>所选适配器的实际命令入口。</summary>
    public string Compatibility => Environment is { CanAccess: true } e ? Text.Format("DockerCompatibility", e.ComposeCommand.Length == 0 ? Text.Get("DockerComposeUnavailable") : e.ComposeCommand) : Text.Get("DockerDetectFirst");
    /// <summary>真实容器标识摘要。</summary>
    public string ContainerHint => SelectedContainer is { } c ? Text.Format("SshMonitorSelectedContainer", c.Id[..Math.Min(12, c.Id.Length)]) : Text.Get("SshMonitorSelectContainer");
    /// <summary>错误横栏可视性。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>参数与能力提示可视性。</summary>
    public bool HasValidationError => ValidationError.Length > 0;
    /// <summary>是否尚未执行。</summary>
    public bool HasNoOutput => Output.Length == 0;

    private void Notify()
    {
        foreach (string property in new[] { nameof(OperationTitle), nameof(Host), nameof(CanEdit), nameof(CanExecute), nameof(CanInsert), nameof(NeedsContainer),
            nameof(IsLogs), nameof(NeedsTimeout), nameof(NeedsImage), nameof(NeedsCompose), nameof(ChangesState), nameof(Preview), nameof(EnvironmentSummary),
            nameof(Compatibility), nameof(ContainerHint), nameof(HasNoOutput), nameof(HasContainer), nameof(HasNoContainer), nameof(HasNoContainers), nameof(HasNoImages), nameof(HasNoImageSnapshot), nameof(IsOtherOperation) }) OnPropertyChanged(property);
        DetectCommand?.NotifyCanExecuteChanged(); ExecuteCommand?.NotifyCanExecuteChanged(); InsertCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged();
        ActionCommand?.NotifyCanExecuteChanged(); ShowDetailsCommand?.NotifyCanExecuteChanged(); ShowResourcesCommand?.NotifyCanExecuteChanged();
        SelectToolCommand?.NotifyCanExecuteChanged();
    }
}
