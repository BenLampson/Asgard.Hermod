namespace Asgard.Hermod.Ssh.UI.Services;

/// <summary>会话生命周期的确认边界，测试可替换模态交互。</summary>
public interface ISshWorkspaceDialogs
{
    /// <summary>确认关闭活动连接和页签，取消不会回收会话。</summary>
    Task<bool> ConfirmCloseAsync(string instanceName);
    /// <summary>整体关闭清楚说明控制台、转发、传输和实例连接影响。</summary>
    Task<bool> ConfirmInstanceCloseAsync(string instanceName, int consoles, int forwards, int transfers) => ConfirmCloseAsync(instanceName);
    /// <summary>单控制台关闭只结束该 Shell，实例工具继续运行。</summary>
    Task<bool> ConfirmConsoleCloseAsync(string instanceName, string consoleName) => ConfirmCloseAsync(instanceName);
    /// <summary>命令执行或文件删除须明确确认，默认实现拒绝变更。</summary>
    Task<bool> ConfirmActionAsync(string title, string detail, string preview) => Task.FromResult(false);
    /// <summary>选择本机上传文件，取消返回空。</summary>
    string? ChooseUpload() => null;
    /// <summary>选择下载目标，由系统文件对话框确认覆盖。</summary>
    string? ChooseDownload(string name) => null;
    /// <summary>输入单个目录或文件名，取消返回空。</summary>
    string? RequestName(string title, string initial) => null;
}
