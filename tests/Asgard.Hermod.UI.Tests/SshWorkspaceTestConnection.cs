using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>附加通道与 PTY 完全独立的测试边界，不连接外部服务器。</summary>
internal sealed class SshWorkspaceTestConnection : ISshWorkspaceConnection
{
    private readonly SshTestConnection _terminal = new();
    private readonly CancellationTokenSource _disconnected = new();
    internal List<SshTestConnection> Consoles { get; } = [];
    internal int DisposeCount { get; private set; }
    internal Exception? ConsoleError { get; set; }
    internal Func<CancellationToken, Task<ISshConsoleConnection>>? ConsoleHandler { get; set; }
    internal SshWorkspaceTestConnection() => Consoles.Add(_terminal);
    public CancellationToken Disconnected => _disconnected.Token;
    public ISshConsoleConnection InitialConsole => _terminal;
    public Task<ISshConsoleConnection> OpenConsoleAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (ConsoleError is { } error) throw error;
        if (ConsoleHandler is not null) return ConsoleHandler(cancellationToken);
        var console = new SshTestConnection(); Consoles.Add(console); console.Resize(columns, rows); return Task.FromResult<ISshConsoleConnection>(console);
    }
    internal void LoseTransport() => _disconnected.Cancel();
    internal List<string> Commands { get; } = [];
    internal IReadOnlyList<SshFileEntry> Entries { get; set; } = [new("folder", "/home/folder", true, 0, "755", DateTimeOffset.UnixEpoch), new("config.conf", "/home/config.conf", false, 42, "644", DateTimeOffset.UnixEpoch)];
    internal List<string> Deleted { get; } = [];
    internal Func<string, CancellationToken, Task<SshCommandResult>>? Handler { get; set; }
    internal Func<Stream, CancellationToken, Task>? DownloadHandler { get; set; }
    internal int FileReads { get; private set; }
    internal List<MemoryStream> ForwardHandles { get; } = [];
    internal void Output(string text) => _terminal.Output(text);
    public IReadOnlyList<TrustedHostKey> HostKeys => _terminal.HostKeys;
    public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken token) => _terminal.ReadAsync(output, token);
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken token) => _terminal.WriteAsync(input, token);
    public void Resize(int columns, int rows) => _terminal.Resize(columns, rows);
    public ValueTask<int> GetExitCodeAsync(CancellationToken token) => _terminal.GetExitCodeAsync(token);
    public async ValueTask DisposeAsync()
    {
        if (DisposeCount > 0) return; DisposeCount++; _disconnected.Cancel();
        foreach (var console in Consoles) await console.DisposeAsync();
    }
    public Task<SshCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    { Commands.Add(command); return Handler?.Invoke(command, cancellationToken) ?? Task.FromResult(new SshCommandResult("test output", 0, false)); }
    public Task<IReadOnlyList<SshFileEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default) { FileReads++; return Task.FromResult(Entries); }
    public Task UploadAsync(Stream source, string path, IProgress<long>? progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken = default) => DownloadHandler?.Invoke(destination, cancellationToken) ?? Task.CompletedTask;
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RenameAsync(string source, string destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(string path, bool directory, CancellationToken cancellationToken = default) { Deleted.Add(path); return Task.CompletedTask; }
    public Task<IDisposable> ForwardAsync(int localPort, string host, int port, CancellationToken cancellationToken = default)
    { var handle = new MemoryStream(); ForwardHandles.Add(handle); return Task.FromResult<IDisposable>(handle); }
}
