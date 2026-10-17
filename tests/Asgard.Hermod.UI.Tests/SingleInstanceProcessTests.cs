using System.Diagnostics;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实独立进程竞争内核所有权和受限 IPC，不以 mock 单例替代启动验证。</summary>
public sealed class SingleInstanceProcessTests
{
    /// <summary>并发启动只有一个主进程，启动中的激活合并保留，退出后可重新启动。</summary>
    [Fact]
    public async Task ConcurrentStarts_QueueActivationAndAllowRestart()
    {
        string scope = "Hermod.Test." + Guid.NewGuid().ToString("N");
        var processes = Enumerable.Range(0, 6).Select(_ => Start(scope)).ToArray();
        try
        {
            var lines = await Task.WhenAll(processes.Select(ReadAsync));
            int ownerIndex = Array.IndexOf(lines, "owner");
            Assert.Equal(1, lines.Count(line => line == "owner"));
            Assert.Equal(5, lines.Count(line => line == "secondary"));
            var owner = processes[ownerIndex];
            await SendAsync(owner, "ready");
            Assert.Equal("activation", await ReadAsync(owner));
            using (var duplicate = Start(scope))
            {
                Assert.Equal("secondary", await ReadAsync(duplicate));
                Assert.Equal("activation", await ReadAsync(owner));
                await duplicate.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.Equal(0, duplicate.ExitCode);
            }
            await SendAsync(owner, "exiting");
            Assert.Equal("exiting", await ReadAsync(owner));
            using var waiting = Start(scope);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Assert.False(waiting.HasExited);
            await SendAsync(owner, "exit");
            await owner.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("owner", await ReadAsync(waiting));
            await SendAsync(waiting, "exit");
            await waiting.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, waiting.ExitCode);
        }
        finally { foreach (var process in processes) { if (!process.HasExited) process.Kill(true); process.Dispose(); } }
    }

    /// <summary>主进程异常终止后弃置互斥量可恢复，不要求手工删除状态文件。</summary>
    [Fact]
    public async Task OwnerCrash_AllowsNextProcessToAcquire()
    {
        string scope = "Hermod.Test." + Guid.NewGuid().ToString("N");
        using var owner = Start(scope);
        Assert.Equal("owner", await ReadAsync(owner));
        owner.Kill(true); await owner.WaitForExitAsync(TestContext.Current.CancellationToken);
        using var next = Start(scope);
        try
        {
            Assert.Equal("owner", await ReadAsync(next));
            await SendAsync(next, "exit");
            await next.WaitForExitAsync(TestContext.Current.CancellationToken);
        }
        finally { if (!next.HasExited) next.Kill(true); }
    }

    private static Process Start(string scope)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Asgard.Hermod.slnx"))) root = root.Parent!;
        string configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal) ? "Release" : "Debug";
        string path = Path.Combine(root.FullName, "tests", "Asgard.Hermod.LifetimeProbe", "bin", configuration, "net10.0-windows", "Asgard.Hermod.LifetimeProbe.exe");
        var info = new ProcessStartInfo(path) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add(scope);
        return Process.Start(info)!;
    }
    private static async Task<string?> ReadAsync(Process process) =>
        await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    private static async Task SendAsync(Process process, string command)
    {
        await process.StandardInput.WriteLineAsync(command); await process.StandardInput.FlushAsync();
    }
}
