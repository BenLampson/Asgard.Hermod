using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>替换连接边界，而不替换工作页的终端或生命周期业务。</summary>
internal sealed class SshTestFactory : ISshConnectionFactory
{
    internal int Calls { get; private set; }
    internal CancellationToken LastToken { get; private set; }
    internal List<SshTestConnection> Connections { get; } = [];
    internal Func<CancellationToken, Task<ISshConnection>>? Handler { get; set; }
    public Task<ISshConnection> OpenAsync(InstanceConnectionOptions options, int columns, int rows, CancellationToken cancellationToken = default)
    {
        Calls++; LastToken = cancellationToken;
        if (Handler is not null) return Handler(cancellationToken);
        var connection = new SshTestConnection(); Connections.Add(connection); return Task.FromResult<ISshConnection>(connection);
    }
}
