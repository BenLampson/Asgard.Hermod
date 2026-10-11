namespace Asgard.Hermod.UI.Services;

/// <summary>将终端连接的创建交给宿主；界面不直接依赖进程或网络 SDK。</summary>
public interface ITerminalSessionFactory
{
    /// <summary>在用户明确打开终端时创建本地会话。</summary>
    ITerminalWorkspaceSession OpenLocal();
}
