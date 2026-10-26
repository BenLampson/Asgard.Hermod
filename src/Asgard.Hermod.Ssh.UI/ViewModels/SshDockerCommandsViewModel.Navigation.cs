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
        }
    } = "";
    /// <summary>资源标签使用稳定 ID。</summary>
    public string ToolSection
    {
        get;
        set
        {
            if (value is not ("containers" or "images" or "compose") || !SetProperty(ref field, value)) return;
            foreach (string property in new[] { nameof(IsContainerTool), nameof(IsImageTool), nameof(IsComposeTool), nameof(IsOtherTool), nameof(VisibleOperations), nameof(HasNoContainer) }) OnPropertyChanged(property);
            if (value != "containers") { SelectedOtherOperation = VisibleOperations.FirstOrDefault(); DetailSection = "output"; }
        }
    } = "containers";
    /// <summary>容器列表与动作。</summary>
    public bool IsContainerTool => ToolSection == "containers";
    /// <summary>镜像工具标签。</summary>
    public bool IsImageTool => ToolSection == "images";
    /// <summary>Compose 工具标签。</summary>
    public bool IsComposeTool => ToolSection == "compose";
    /// <summary>镜像和 Compose 的显式参数表单。</summary>
    public bool IsOtherTool => !IsContainerTool;
    /// <summary>只列出当前资源类型支持的操作。</summary>
    public IReadOnlyList<SshDockerOperationItem> VisibleOperations => OtherOperations.Where(o => IsImageTool ? o.Id is "images" or "pull" : o.Definition.RequiresCompose).ToArray();
    /// <summary>详情标签独立于最终执行结果的固定目标。</summary>
    public string DetailSection
    {
        get;
        set
        {
            if (value is not ("resources" or "output" or "details") || !SetProperty(ref field, value)) return;
            foreach (string property in new[] { nameof(IsResourceSection), nameof(IsOutputSection), nameof(IsDetailsSection) }) OnPropertyChanged(property);
        }
    } = "resources";
    /// <summary>真实资源监测在下半区展示。</summary>
    public bool IsResourceSection => DetailSection == "resources";
    /// <summary>日志与操作结果。</summary>
    public bool IsOutputSection => DetailSection == "output";
    /// <summary>当前容器只读身份详情。</summary>
    public bool IsDetailsSection => DetailSection == "details";
    /// <summary>切换容器、镜像与 Compose。</summary>
    public RelayCommand SelectToolCommand { get; private set; } = null!;
    /// <summary>切换详情标签，不自动执行日志或其他远端命令。</summary>
    public RelayCommand SelectDetailCommand { get; private set; } = null!;

    /// <summary>独立采样结果补充列表资源字段，不覆盖正在确认或执行的目标。</summary>
    public void UpdateResources(IEnumerable<SshMonitorContainerItem> items)
    {
        if (IsBusy || _disposed || !IsInitialized) return;
        UpdateContainers(items.ToArray());
    }
}
