using System.Diagnostics;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>显式启用时，通过本机无网络的 Debian 容器验证真实 Linux PTY；不连接远端堡垒机。</summary>
public sealed class SshBastionPtyTests
{
    /// <summary>验证真实终端回显、规范模式行限制、Unicode、退出码及输出截断。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(12000)]
    [InlineData(70000)]
    public async Task LinuxPtyPreservesCommandFrames(int length)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HERMOD_BASTION_PTY_TEST") == "1", "Set HERMOD_BASTION_PTY_TEST=1 with the local debian:12 image available.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var raw = new ContainerConsole();
        await using var selected = await SshBastionConsole.SelectAsync(raw, SshBastionTests.Options, timeout.Token);
        string expected = "中文\r\nquotes ' \u001b[31mred\u001b[0m";
        string command = length switch
        {
            0 => "printf '%s' \"中文\r\nquotes ' \"; printf '\\033[31mred\\033[0m'; exit 42",
            12000 => "printf '%s' '" + new string('x', length) + "'; exit 42",
            _ => "head -c 70000 /dev/zero | tr '\\000' x; exit 42",
        };
        var result = await new SshBastionCommand().ExecuteAsync(selected, command, timeout.Token);
        Assert.Equal(length > 65536, result.Truncated);
        Assert.Equal(length > 65536 ? -1 : 42, result.ExitCode);
        Assert.Equal(length == 0 ? expected : new string('x', Math.Min(length, 65536)), result.Output);
    }

    /// <summary>仅拥有自己创建的容器和进程；关闭通道不会操作其他本机容器。</summary>
    private sealed class ContainerConsole : ISshConsoleConnection
    {
        private readonly string _name = "hermod-bastion-test-" + Guid.NewGuid().ToString("N");
        private readonly Process _process;
        private readonly Task<string> _errors;
        private bool _disposed;

        public ContainerConsole()
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            string[] arguments = ["run", "--rm", "-i", "--network", "none", "--name", _name, "debian:12", "script", "-q", "-e", "-c",
                "printf '[vbh]> '; read number; test \"$number\" = 2 || exit 2; PS1='target# ' exec sh -i", "/dev/null"];
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the local Docker fixture.");
            _errors = _process.StandardError.ReadToEndAsync();
        }

        public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken) => _process.StandardOutput.BaseStream.ReadAsync(output, cancellationToken);
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            await _process.StandardInput.BaseStream.WriteAsync(input, cancellationToken);
            await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        }
        public void Resize(int columns, int rows) { }
        public async ValueTask<int> GetExitCodeAsync(CancellationToken cancellationToken)
        {
            await _process.WaitForExitAsync(cancellationToken);
            return _process.ExitCode;
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cleanup = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "rm", "-f", _name }) cleanup.ArgumentList.Add(argument);
            using var remove = Process.Start(cleanup)!;
            await remove.WaitForExitAsync(timeout.Token);
            if (!_process.HasExited) _process.Kill(true);
            await _process.WaitForExitAsync(timeout.Token);
            await _errors;
            _process.Dispose();
        }
    }
}
