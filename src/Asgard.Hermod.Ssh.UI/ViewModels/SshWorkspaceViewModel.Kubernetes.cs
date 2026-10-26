namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>kubectl 管理属于实例窗口，与 Docker 管理并列，复用当前 SSH 连接。</summary>
public sealed partial class SshWorkspaceViewModel
{
    /// <summary>远端 kubectl 的上下文、资源与执行结果。</summary>
    public SshKubernetesViewModel Kubernetes { get; private set; } = null!;
    /// <summary>收起页面保留查询范围。</summary>
    public bool IsKubernetesOpen
    {
        get => ActiveSection == "kubernetes";
        set { if (value) ActiveSection = "kubernetes"; else if (IsKubernetesOpen) ActiveSection = "terminal"; }
    }
    /// <summary>独立的标题栏管理入口。</summary>
    public AsyncRelayCommand ToggleKubernetesCommand { get; private set; } = null!;

    private void InitializeKubernetes()
    {
        Kubernetes = new(() => IsConnected && !IsClosed ? _connection as ISshWorkspaceConnection : null, () => RemoteIdentity, _dialogs);
        ToggleKubernetesCommand = new AsyncRelayCommand(async _ =>
        {
            IsKubernetesOpen = !IsKubernetesOpen;
            if (IsKubernetesOpen) await Kubernetes.InitializeAsync();
        }, _ => !IsClosed && SupportsInstanceTools);
    }
}
