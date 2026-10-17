using System.Diagnostics;
using System.IO.Pipes;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>隔离命名管道桥接测试容器的真实 OpenSSH Agent，不读取开发机 Agent。</summary>
internal sealed class LiveAgentPipe : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _clients = [];
    private readonly Task _accept;
    private readonly string _container;
    private readonly string _name = "hermod-test-agent-" + Guid.NewGuid().ToString("N");
    internal string Address => @"\\.\pipe\" + _name;
    internal LiveAgentPipe(string container) { _container = container; _accept = AcceptAsync(); }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try { await pipe.WaitForConnectionAsync(_stop.Token); }
                catch { pipe.Dispose(); throw; }
                // SDK 的 File.Exists 会先建立一个探测连接，不能阻止下一连接进入。
                _clients.Add(RelayAsync(pipe));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RelayAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var process = new Process())
        {
            process.StartInfo = new("docker") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in new[] { "exec", "-i", _container, "socat", "STDIO", "UNIX-CONNECT:/tmp/hermod-agent.sock" }) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            Task errors = process.StandardError.ReadToEndAsync(_stop.Token);
            using var relay = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            Task send = pipe.CopyToAsync(process.StandardInput.BaseStream, relay.Token);
            Task receive = process.StandardOutput.BaseStream.CopyToAsync(pipe, relay.Token);
            await Task.WhenAny(send, receive); relay.Cancel();
            try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { } catch (IOException) { }
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await errors; } catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    { _stop.Cancel(); await _accept; await Task.WhenAll(_clients); _stop.Dispose(); }
}
