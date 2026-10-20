using System.Text;
using System.Threading.Channels;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>可逐段投递输出的 PTY 替身，用于验证菜单状态机而不是模拟普通 exec 成功。</summary>
internal sealed class BastionTestConsole : ISshConsoleConnection
{
    private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
    private ReadOnlyMemory<byte> _remaining;
    internal List<string> Inputs { get; } = [];
    internal Action<string>? Written { get; set; }
    internal bool Disposed { get; private set; }
    internal void Output(string value) => _output.Writer.TryWrite(Encoding.UTF8.GetBytes(value));
    internal void End() => _output.Writer.TryComplete();
    public async ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken)
    {
        if (_remaining.Length == 0)
        {
            if (!await _output.Reader.WaitToReadAsync(cancellationToken)) return 0;
            _remaining = await _output.Reader.ReadAsync(cancellationToken);
        }
        int count = Math.Min(output.Length, _remaining.Length);
        _remaining[..count].CopyTo(output); _remaining = _remaining[count..]; return count;
    }
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string value = Encoding.UTF8.GetString(input.Span); Inputs.Add(value); Written?.Invoke(value); return ValueTask.CompletedTask;
    }
    public void Resize(int columns, int rows) { }
    public ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0);
    public ValueTask DisposeAsync() { Disposed = true; End(); return ValueTask.CompletedTask; }
}
