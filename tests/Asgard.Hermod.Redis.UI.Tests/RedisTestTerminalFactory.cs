using Asgard.Hermod.WinTerminal.Services;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>主窗口测试不能意外打开本机进程。</summary>
internal sealed class RedisTestTerminalFactory : ITerminalSessionFactory
{
    public ITerminalWorkspaceSession OpenLocal() => throw new NotSupportedException();
}
