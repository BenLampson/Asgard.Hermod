namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>命令库与监测共用工作区，不创建新的实例或复制 SSH 凭据。</summary>
public partial class SshWorkspaceView
{
    private void OnCloseDockerCommands(object? sender, EventArgs args) { if (_model is not null) _model.IsDockerCommandsOpen = false; }
    private void OnCloseDockerResources(object? sender, EventArgs args) { if (_model is not null) _model.IsDockerResourcesOpen = false; }
    private void OnCloseKubernetes(object? sender, EventArgs args) { if (_model is not null) _model.IsKubernetesOpen = false; }
    private async void OnDockerMonitorRequested(object? sender, EventArgs args)
    {
        if (_model is null) return;
        await _model.ShowDockerResourcesCommand.ExecuteAsync();
    }
}
