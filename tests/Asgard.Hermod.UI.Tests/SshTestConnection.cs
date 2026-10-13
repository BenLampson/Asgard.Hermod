using System.Text;
using System.Threading.Channels;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>可控的协议 I/O 边界，保留输入顺序、终止与迟到连接能力。</summary>
internal sealed class SshTestConnection : ISshConnection
{
    private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
    internal List<string> Inputs { get; } = [];
    internal int DisposeCount { get; private set; }
    internal (int Columns, int Rows) Size { get; private set; }
    public IReadOnlyList<TrustedHostKey> HostKeys { get; } = [new("target.example", 22, "ssh-ed25519", "SHA256:" + new string('A', 43))];
    internal void Output(string text) => _output.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
    internal void End() => _output.Writer.TryComplete();
    public async ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken)
    {
        if (!await _output.Reader.WaitToReadAsync(cancellationToken)) return 0;
        byte[] bytes = await _output.Reader.ReadAsync(cancellationToken); bytes.CopyTo(output); return bytes.Length;
    }
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); lock (Inputs) Inputs.Add(Encoding.UTF8.GetString(input.Span)); return ValueTask.CompletedTask; }
    public void Resize(int columns, int rows) => Size = (columns, rows);
    public ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0);
    public ValueTask DisposeAsync() { if (DisposeCount == 0) { DisposeCount++; End(); } return ValueTask.CompletedTask; }
}
