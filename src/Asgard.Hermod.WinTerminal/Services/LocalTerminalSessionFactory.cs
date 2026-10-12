namespace Asgard.Hermod.WinTerminal.Services;

/// <summary>在宿主层创建真实的本地 PowerShell 连接。</summary>
public sealed class LocalTerminalSessionFactory : ITerminalSessionFactory
{
    /// <inheritdoc />
    public ITerminalWorkspaceSession OpenLocal()
    {
        var terminal = new TerminalSession(120, 32);
        try
        {
            var connection = ConPtyConnection.Start(terminal,
                "powershell.exe -NoLogo -NoProfile", Environment.CurrentDirectory);
            return new LocalTerminalWorkspaceSession(terminal, connection);
        }
        catch
        {
            terminal.Dispose();
            throw;
        }
    }
}
