namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>会话 UI 状态使用稳定枚举，翻译文案不影响输入或重连行为。</summary>
public enum SshWorkspacePhase
{
    /// <summary>尚未建立连接，允许取消。</summary>
    Connecting,
    /// <summary>身份、认证与 PTY 已完成。</summary>
    Connected,
    /// <summary>正常结束或用户断开，保留只读输出。</summary>
    Disconnected,
    /// <summary>连接失败，保留原有输出并提供安全错误。</summary>
    Failed,
}
