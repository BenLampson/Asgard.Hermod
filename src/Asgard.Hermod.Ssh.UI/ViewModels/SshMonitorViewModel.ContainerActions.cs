namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>容器行只负责选择，所有查看入口由底部操作栏提供。</summary>
public sealed partial class SshMonitorViewModel
{
    /// <summary>详情展示已采集的结构化字段，不额外读取环境变量或凭据。</summary>
    public RelayCommand ViewContainerDetailsCommand { get; }
    /// <summary>详情抽屉只显示结构化字段；日志使用独立窗口。</summary>
    public bool IsContainerDetailsOpen { get; private set { if (SetProperty(ref field, value)) NotifyContainerPane(); } }
    /// <summary>宽屏并排、窄屏覆盖列表的统一显隐状态。</summary>
    public bool IsContainerPaneOpen => IsContainerDetailsOpen;
    /// <summary>没有选中项时显示明确提示。</summary>
    public string SelectedContainerName => SelectedContainer?.Name ?? Text.Get("SshMonitorSelectContainer");
    /// <summary>底部只显示短 ID，完整 ID 在详情中展示。</summary>
    public string SelectedContainerSummary => SelectedContainer is { } item ? Text.Format("SshMonitorSelectedContainer", item.Id[..Math.Min(12, item.Id.Length)]) : "";
    /// <summary>抽屉标题与实际内容保持一致。</summary>
    public string ContainerPaneTitle => Text.Format("SshMonitorContainerDetailsTitle", SelectedContainer?.Name ?? "");
    /// <summary>关闭按钮的无障碍名称随内容变化。</summary>
    public string ContainerPaneCloseLabel => Text.Get("SshMonitorCloseDetails");

    private bool IsCurrentContainer(SshMonitorContainerItem? item) => IsDocker && item is not null &&
        ContainerAvailability == SshContainerAvailability.Available && Containers.Any(c => c.Id == item.Id) && ContainerView.Contains(item);

    /// <summary>隐藏或移除的行不能继续作为操作目标。</summary>
    private void ValidateContainerSelection()
    {
        if (SelectedContainer is not null && !IsCurrentContainer(SelectedContainer)) SelectedContainer = null;
        NotifyContainerSelection();
    }

    private void NotifyContainerSelection()
    {
        OnPropertyChanged(nameof(SelectedContainerName)); OnPropertyChanged(nameof(SelectedContainerSummary));
        ReadLogsCommand.NotifyCanExecuteChanged(); ViewContainerDetailsCommand.NotifyCanExecuteChanged(); NotifyContainerPane();
        OpenDockerCommandsCommand?.NotifyCanExecuteChanged(); StartContainerCommand?.NotifyCanExecuteChanged(); RestartContainerCommand?.NotifyCanExecuteChanged(); StopContainerCommand?.NotifyCanExecuteChanged();
        NotifyDockerResources();
    }

    private void NotifyContainerPane()
    {
        OnPropertyChanged(nameof(IsContainerPaneOpen)); OnPropertyChanged(nameof(ContainerPaneTitle)); OnPropertyChanged(nameof(ContainerPaneCloseLabel));
    }

    private void CloseContainerPane() => IsContainerDetailsOpen = false;
}
