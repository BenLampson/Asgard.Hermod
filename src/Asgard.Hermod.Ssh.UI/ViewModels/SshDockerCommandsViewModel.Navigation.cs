namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>并排管理面板的资源与详情标签只改变展示，不执行远端变更。</summary>
public sealed partial class SshDockerCommandsViewModel
{
    /// <summary>完整环境和容器列表加载成功后缓存就绪状态，失败仍能一键重试。</summary>
    public bool IsInitialized { get; private set => SetProperty(ref field, value); }
    /// <summary>状态筛选在本机完成，被隐藏的容器立即失去动作权限。</summary>
    public string ContainerState
    {
        get;
        set
        {
            if (value is not ("" or "running" or "exited") || !SetProperty(ref field, value)) return;
            ContainerView.Refresh();
            if (SelectedContainer is { } item && !ContainerView.Contains(item)) SelectedContainer = null;
            OnPropertyChanged(nameof(HasNoContainers));
            OnPropertyChanged(nameof(ContainerEmptyMessage));
        }
    } = "";
    /// <summary>资源标签使用稳定 ID。</summary>
    public string ToolSection
    {
        get;
        set
        {
            if (value is not ("containers" or "images" or "compose" or "resources" or "details") || !SetProperty(ref field, value)) return;
            foreach (string property in new[] { nameof(IsContainerTool), nameof(IsImageTool), nameof(IsComposeTool), nameof(IsResourceTool), nameof(IsDetailsTool), nameof(VisibleOperations), nameof(HasNoContainer) }) OnPropertyChanged(property);
            if (value == "images") SelectedOtherOperation = OtherOperations.First(o => o.Id == "pull");
            else if (value == "compose") SelectedOtherOperation = VisibleOperations.FirstOrDefault();
        }
    } = "containers";
    /// <summary>容器列表与动作。</summary>
    public bool IsContainerTool => ToolSection == "containers";
    /// <summary>镜像工具标签。</summary>
    public bool IsImageTool => ToolSection == "images";
    /// <summary>Compose 工具标签。</summary>
    public bool IsComposeTool => ToolSection == "compose";
    /// <summary>所选容器的资源用量是主内容，而非命令结果。</summary>
    public bool IsResourceTool => ToolSection == "resources";
    /// <summary>所选容器的身份详情是主内容，而非命令结果。</summary>
    public bool IsDetailsTool => ToolSection == "details";
    /// <summary>Compose 表单只列出对应的操作。</summary>
    public IReadOnlyList<SshDockerOperationItem> VisibleOperations => OtherOperations.Where(o => o.Definition.RequiresCompose).ToArray();
    /// <summary>运行结果默认收起，可由用户随时关闭或重新打开。</summary>
    public bool IsOutputOpen
    {
        get;
        set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(OutputToggleLabel));
        }
    }
    /// <summary>开关标签描述下一次操作。</summary>
    public string OutputToggleLabel => Text.Get(IsOutputOpen ? "DockerHideOutput" : "DockerShowOutput");
    /// <summary>切换容器、镜像、Compose 和容器详情。</summary>
    public RelayCommand SelectToolCommand { get; private set; } = null!;
    /// <summary>开关运行结果，不改变上方的主内容。</summary>
    public RelayCommand ToggleOutputCommand { get; private set; } = null!;

    /// <summary>独立采样结果补充列表资源字段，不覆盖正在确认或执行的目标。</summary>
    public void UpdateResources(IEnumerable<SshMonitorContainerItem> items)
    {
        if (IsBusy || _disposed || !IsInitialized) return;
        UpdateContainers(items.ToArray());
    }
}
