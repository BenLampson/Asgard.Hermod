using Asgard.Hermod.Abstractions.Models;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>只提供 PTY 的堡垒机，刻意不实现任何 exec、SFTP 或 TCP 转发接口。</summary>
internal sealed class BastionTestConnection : ISshConnection
{
    internal List<BastionTestConsole> Consoles { get; } = [];
    internal Action<BastionTestConsole, string>? Written { get; set; }
    internal BastionTestConnection() => InitialConsole = Create();
    private BastionTestConsole Create()
    {
        var console = new BastionTestConsole(); Consoles.Add(console);
        console.Output("Welcome\r\n[vbh]> ");
        console.Written = input =>
        {
            if (input == "2\r") console.Output("2\r\nWelcome to selected host\r\nroot@selected:~# ");
            else Written?.Invoke(console, input);
        };
        return console;
    }
    public ISshConsoleConnection InitialConsole { get; }
    public IReadOnlyList<TrustedHostKey> HostKeys => [];
    public Task<ISshConsoleConnection> OpenConsoleAsync(int columns, int rows, CancellationToken cancellationToken = default) => Task.FromResult<ISshConsoleConnection>(Create());
    public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken) => InitialConsole.ReadAsync(output, cancellationToken);
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => InitialConsole.WriteAsync(input, cancellationToken);
    public void Resize(int columns, int rows) => InitialConsole.Resize(columns, rows);
    public ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken) => InitialConsole.GetExitCodeAsync(cancellationToken);
    public async ValueTask DisposeAsync() { foreach (var console in Consoles) await console.DisposeAsync(); }
}
