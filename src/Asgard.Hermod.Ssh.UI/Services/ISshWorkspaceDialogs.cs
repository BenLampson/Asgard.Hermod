namespace Asgard.Hermod.Ssh.UI.Services;

/// <summary>会话生命周期的确认边界，测试可替换模态交互。</summary>
public interface ISshWorkspaceDialogs
{
    /// <summary>确认关闭活动连接和页签，取消不会回收会话。</summary>
    Task<bool> ConfirmCloseAsync(string instanceName);
}
