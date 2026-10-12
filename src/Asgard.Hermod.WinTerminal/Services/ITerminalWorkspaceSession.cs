namespace Asgard.Hermod.WinTerminal.Services;

/// <summary>工作区持有的终端会话句柄，同时拥有连接与缓冲区的释放职责。</summary>
public interface ITerminalWorkspaceSession : IAsyncDisposable
{
    /// <summary>用于绑定 WPF 控件的终端状态。</summary>
    TerminalSession Terminal { get; }
    /// <summary>显示正在使用的 Shell 名称。</summary>
    string DisplayName { get; }
    /// <summary>在进程退出且最后输出收尾后完成。</summary>
    Task<int> Completion { get; }
}
