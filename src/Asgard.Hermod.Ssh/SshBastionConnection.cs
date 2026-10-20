namespace Asgard.Hermod.Ssh;

/// <summary>菜单型堡垒机适配：终端和每个工具命令独立选机，禁止把文件和转发请求落到堡垒机。</summary>
internal sealed class SshBastionConnection(ISshConnection transport, ISshConsoleConnection initial, SshConnectionOptions options) : ISshWorkspaceConnection
{
    /// <summary>初始会话成功选机以后才发布工作区连接。</summary>
    internal static async Task<SshBastionConnection> CreateAsync(ISshConnection transport, SshConnectionOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var selected = await SshBastionConsole.SelectAsync(transport.InitialConsole, options, cancellationToken).ConfigureAwait(false);
            return new(transport, selected, options);
        }
        catch { await transport.DisposeAsync().ConfigureAwait(false); throw; }
    }
    /// <inheritdoc />
    public IReadOnlyList<TrustedHostKey> HostKeys => transport.HostKeys;
    /// <inheritdoc />
    public CancellationToken Disconnected => transport.Disconnected;
    /// <inheritdoc />
    public ISshConsoleConnection InitialConsole => initial;
    /// <inheritdoc />
    public async Task<ISshConsoleConnection> OpenConsoleAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        var console = await transport.OpenConsoleAsync(columns, rows, cancellationToken).ConfigureAwait(false);
        return await SshBastionConsole.SelectAsync(console, options, cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc />
    public async Task<SshCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await using var console = await OpenConsoleAsync(120, 32, cancellationToken).ConfigureAwait(false);
        return await new SshBastionCommand().ExecuteAsync(console, command, cancellationToken).ConfigureAwait(false);
    }
    /// <inheritdoc />
    public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken) => initial.ReadAsync(output, cancellationToken);
    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => initial.WriteAsync(input, cancellationToken);
    /// <inheritdoc />
    public void Resize(int columns, int rows) => initial.Resize(columns, rows);
    /// <inheritdoc />
    public ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken) => initial.GetExitCodeAsync(cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync() => transport.DisposeAsync();
    /// <inheritdoc />
    public Task<IReadOnlyList<SshFileEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw Unsupported();
    /// <inheritdoc />
    public Task UploadAsync(Stream source, string path, IProgress<long>? progress, CancellationToken cancellationToken = default) => throw Unsupported();
    /// <inheritdoc />
    public Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken = default) => throw Unsupported();
    /// <inheritdoc />
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw Unsupported();
    /// <inheritdoc />
    public Task RenameAsync(string source, string destination, CancellationToken cancellationToken = default) => throw Unsupported();
    /// <inheritdoc />
    public Task DeleteAsync(string path, bool directory, CancellationToken cancellationToken = default) => throw Unsupported();
    /// <inheritdoc />
    public Task<IDisposable> ForwardAsync(int localPort, string host, int port, CancellationToken cancellationToken = default) => throw Unsupported();

    private static SshException Unsupported() => new("SshBastionToolsUnsupported", "SSH_BASTION_TOOLS_UNSUPPORTED");
}
