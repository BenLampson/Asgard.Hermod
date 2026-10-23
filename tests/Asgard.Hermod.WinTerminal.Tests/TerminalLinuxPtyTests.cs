using System.Diagnostics;
using System.Threading.Channels;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>显式启用时用本机隔离 Linux PTY 验证历史重绘，不连接用户服务器。</summary>
[Trait("Category", "Integration")]
public sealed class TerminalLinuxPtyTests
{
    /// <summary>长草稿在正确的列宽内不换行，上下键分别调取历史和恢复草稿。</summary>
    [Fact]
    public async Task HistoryArrowsPreservePromptRowAndLongDraft()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HERMOD_TERMINAL_PTY_TEST") == "1", "Set HERMOD_TERMINAL_PTY_TEST=1 with the local debian:12 image available.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        string name = "hermod-terminal-test-" + Guid.NewGuid().ToString("N");
        using var session = new TerminalSession(180, 30);
        var inputs = Channel.CreateUnbounded<byte[]>();
        session.InputReceived += (_, bytes) => inputs.Writer.TryWrite(bytes.ToArray());
        var start = new ProcessStartInfo("docker") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "run", "--rm", "-i", "--network", "none", "--name", name, "debian:12", "script", "-q", "-e", "-c",
            "stty cols 180 rows 30; TERM=xterm-256color HISTFILE=/dev/null PS1='hermod# ' bash --noprofile --norc -i", "/dev/null" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        Task read = ReadAsync(), write = WriteAsync();
        try
        {
            await WaitForLineAsync("hermod#");
            session.SendInput("printf 'history-marker\\n'\r"u8);
            await WaitForLineAsync("hermod#", "history-marker");
            string draft = new('q', 140);
            session.SendInput(Encoding.UTF8.GetBytes(draft));
            await WaitForLineAsync("hermod# " + draft);
            int row = session.Decoder.CursorPosition.Y;
            session.SendInput(Encoding.UTF8.GetBytes(KeyboardEncoder.Encode(TerminalKey.Up, TerminalModifier.None, session.Decoder.State.Modes)!));
            await WaitForLineAsync("hermod# printf 'history-marker\\n'");
            Assert.Equal(row, session.Decoder.CursorPosition.Y);
            session.SendInput(Encoding.UTF8.GetBytes(KeyboardEncoder.Encode(TerminalKey.Down, TerminalModifier.None, session.Decoder.State.Modes)!));
            await WaitForLineAsync("hermod# " + draft);
            Assert.Equal(row, session.Decoder.CursorPosition.Y);
        }
        finally
        {
            timeout.Cancel(); inputs.Writer.TryComplete(); process.StandardInput.Close();
            var cleanup = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "rm", "-f", name }) cleanup.ArgumentList.Add(argument);
            using var remove = Process.Start(cleanup)!;
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await remove.WaitForExitAsync(cleanupTimeout.Token);
            if (!process.HasExited) process.Kill(true);
            try { await Task.WhenAll(read, write, errors); } catch (OperationCanceledException) { }
        }

        async Task ReadAsync()
        {
            byte[] buffer = new byte[8192];
            while (await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token) is > 0 and var count)
                session.FeedOutput(buffer.AsSpan(0, count));
        }
        async Task WriteAsync()
        {
            await foreach (var bytes in inputs.Reader.ReadAllAsync(timeout.Token))
            {
                await process.StandardInput.BaseStream.WriteAsync(bytes, timeout.Token);
                await process.StandardInput.BaseStream.FlushAsync(timeout.Token);
            }
        }
        async Task WaitForLineAsync(string line, string? output = null)
        {
            while (true)
            {
                lock (session.Buffer.SyncRoot)
                {
                    if (session.GetVisibleText().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.TrimEnd() == line
                        && (output is null || session.GetAllText().Split('\n').Any(item => item.TrimEnd() == output))) return;
                }
                await Task.Delay(20, timeout.Token);
            }
        }
    }
}
