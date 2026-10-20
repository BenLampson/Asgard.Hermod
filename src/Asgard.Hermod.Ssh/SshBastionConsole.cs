using System.Text;
using System.Text.RegularExpressions;

namespace Asgard.Hermod.Ssh;

/// <summary>每个 PTY 独立完成菜单选择，并将登录输出原样交还终端；不复制旧控制台输入。</summary>
internal sealed class SshBastionConsole(ISshConsoleConnection inner, byte[] initialOutput) : ISshConsoleConnection
{
    private int _offset;
    private static readonly Regex Escapes = new(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\))",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));

    /// <summary>只有完整提示符到达才发送一次编号；失败释放当前 PTY，不重试或猜测其他编号。</summary>
    internal static async Task<ISshConsoleConnection> SelectAsync(ISshConsoleConnection console, SshConnectionOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var transcript = new MemoryStream();
        try
        {
            await WaitAsync(false).ConfigureAwait(false);
            await console.WriteAsync(Encoding.UTF8.GetBytes(options.BastionSelection + "\r"), timeout.Token).ConfigureAwait(false);
            await WaitAsync(true).ConfigureAwait(false);
            return new SshBastionConsole(console, transcript.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await console.DisposeAsync().ConfigureAwait(false);
            throw new SshException("SshBastionSelectionTimeout", "SSH_BASTION_SELECTION_TIMEOUT");
        }
        catch
        {
            await console.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        async Task WaitAsync(bool selected)
        {
            using var stage = new MemoryStream();
            byte[] buffer = new byte[8192];
            while (true)
            {
                int count = await console.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (count == 0) throw new SshException("SshBastionSelectionFailed", "SSH_BASTION_SELECTION_FAILED");
                transcript.Write(buffer, 0, count); stage.Write(buffer, 0, count);
                if (transcript.Length > 262144) throw new SshException("SshBastionSelectionFailed", "SSH_BASTION_SELECTION_FAILED");
                string text = Escapes.Replace(Encoding.UTF8.GetString(stage.GetBuffer(), 0, (int)stage.Length), "").TrimEnd();
                bool menu = text.EndsWith(options.BastionMenuPrompt, StringComparison.Ordinal);
                if (!selected && menu) return;
                if (selected && menu) throw new SshException("SshBastionSelectionFailed", "SSH_BASTION_SELECTION_FAILED");
                if (selected && (options.BastionShellPrompt.Length > 0
                    ? text.EndsWith(options.BastionShellPrompt, StringComparison.Ordinal)
                    : text.EndsWith('$') || text.EndsWith('#'))) return;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_offset >= initialOutput.Length) return inner.ReadAsync(output, cancellationToken);
        int count = Math.Min(output.Length, initialOutput.Length - _offset);
        initialOutput.AsMemory(_offset, count).CopyTo(output); _offset += count;
        return ValueTask.FromResult(count);
    }
    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => inner.WriteAsync(input, cancellationToken);
    /// <inheritdoc />
    public void Resize(int columns, int rows) => inner.Resize(columns, rows);
    /// <inheritdoc />
    public ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken) => inner.GetExitCodeAsync(cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
