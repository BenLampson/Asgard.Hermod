using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>附加通道与 PTY 完全独立的测试边界，不连接外部服务器。</summary>
internal sealed class SshWorkspaceTestConnection : ISshWorkspaceConnection
{
    private readonly SshTestConnection _terminal = new();
    internal List<string> Commands { get; } = [];
    internal IReadOnlyList<SshFileEntry> Entries { get; set; } = [new("folder", "/home/folder", true, 0, "755", DateTimeOffset.UnixEpoch), new("config.conf", "/home/config.conf", false, 42, "644", DateTimeOffset.UnixEpoch)];
    internal List<string> Deleted { get; } = [];
    internal Func<string, CancellationToken, Task<SshCommandResult>>? Handler { get; set; }
    internal Func<Stream, CancellationToken, Task>? DownloadHandler { get; set; }
    internal int FileReads { get; private set; }
    internal void Output(string text) => _terminal.Output(text);
    public IReadOnlyList<TrustedHostKey> HostKeys => _terminal.HostKeys;
    public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken token) => _terminal.ReadAsync(output, token);
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken token) => _terminal.WriteAsync(input, token);
    public void Resize(int columns, int rows) => _terminal.Resize(columns, rows);
    public ValueTask<int> GetExitCodeAsync(CancellationToken token) => _terminal.GetExitCodeAsync(token);
    public ValueTask DisposeAsync() => _terminal.DisposeAsync();
    public Task<SshCommandResult> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    { Commands.Add(command); return Handler?.Invoke(command, cancellationToken) ?? Task.FromResult(new SshCommandResult("test output", 0, false)); }
    public Task<IReadOnlyList<SshFileEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default) { FileReads++; return Task.FromResult(Entries); }
    public Task UploadAsync(Stream source, string path, IProgress<long>? progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DownloadAsync(string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken = default) => DownloadHandler?.Invoke(destination, cancellationToken) ?? Task.CompletedTask;
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RenameAsync(string source, string destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(string path, bool directory, CancellationToken cancellationToken = default) { Deleted.Add(path); return Task.CompletedTask; }
    public Task<IDisposable> ForwardAsync(int localPort, string host, int port, CancellationToken cancellationToken = default) => Task.FromResult<IDisposable>(new MemoryStream());
}
