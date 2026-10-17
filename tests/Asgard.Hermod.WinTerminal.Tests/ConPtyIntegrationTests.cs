namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>通过真实 Windows Shell 验证进程启动、工作目录、输入、缩放与退出收尾。</summary>
[Trait("Category", "Integration")]
public sealed class ConPtyIntegrationTests
{
    private static string Shell => Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>快速退出的命令也必须保留最后输出、退出码及指定工作目录。</summary>
    [Fact]
    public async Task Command_RetainsFinalOutputWorkingDirectoryAndExitCode()
    {
        using var session = new TerminalSession();
        string directory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        await using var connection = ConPtyConnection.Start(session, $"\"{Shell}\" /d /q /c \"echo HERMOD_COMPLETE&cd&exit /b 7\"", directory);
        int code = await connection.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(7, code);
        Assert.Contains("HERMOD_COMPLETE", session.GetAllText());
        Assert.Contains(directory, session.GetAllText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>交互式输入经过真实 Shell 执行，尺寸调整后仍能继续输入并正常退出。</summary>
    [Fact]
    public async Task InteractiveShell_AcceptsInputResizeAndExit()
    {
        using var session = new TerminalSession();
        await using var connection = ConPtyConnection.Start(session, $"\"{Shell}\" /d /q");
        session.SendInput("echo __HER^MOD_READY__\r"u8);
        await WaitForOutputAsync(session, "__HERMOD_READY__");
        session.Resize(96, 31);
        session.SendInput("echo __RESI^ZED__\r"u8);
        await WaitForOutputAsync(session, "__RESIZED__");
        session.SendInput("exit /b 3\r"u8);
        Assert.Equal(3, await connection.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        Assert.Equal((96, 31), (session.Buffer.Columns, session.Buffer.Rows));
    }

    /// <summary>主动关闭一个已启动的 Shell 时，两个同步通信线程必须及时收尾。</summary>
    [Fact]
    public async Task Dispose_StopsInteractiveShellWithoutDeadlock()
    {
        using var session = new TerminalSession();
        var connection = ConPtyConnection.Start(session, $"\"{Shell}\" /d /q");
        try
        {
            session.SendInput("echo __DIS^POSE_READY__\r"u8);
            await WaitForOutputAsync(session, "__DISPOSE_READY__");
        }
        finally
        {
            await connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        }
        Assert.True(connection.Completion.IsCompletedSuccessfully);
    }

    /// <summary>PowerShell 经 ConPTY 输出中文，退出后保留输出和退出码。</summary>
    [Fact]
    public async Task PowerShell_RetainsUnicodeOutputAndExitCode()
    {
        using var session = new TerminalSession();
        string shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        // 测试生产者显式输出 UTF-8；英语 Windows 的默认 OEM 编码无法表示中文，不能把源头的问号归因于终端解码。
        await using var connection = ConPtyConnection.Start(session,
            $"\"{shell}\" -NoLogo -NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); [Console]::Write('中文终端'); exit 9\"");
        Assert.Equal(9, await connection.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
        Assert.Contains("中文终端", session.GetAllText());
    }

    /// <summary>无效命令启动失败后不保留控制台与管道。</summary>
    [Fact]
    public void MissingExecutable_FailsWithoutOpeningShell()
    {
        using var session = new TerminalSession();
        Assert.Throws<System.ComponentModel.Win32Exception>(() => ConPtyConnection.Start(session, "hermod_missing_executable_7bfbc756.exe"));
        Assert.False(session.IsDisposed);
    }

    /// <summary>异步观察缓冲更新，不依赖固定延迟或命令行回显中的相同字符串。</summary>
    private static async Task WaitForOutputAsync(TerminalSession session, string marker)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs args)
        {
            if (session.GetAllText().Contains(marker, StringComparison.Ordinal))
                completed.TrySetResult();
        }
        session.BufferUpdated += Check;
        try
        {
            Check(null, EventArgs.Empty);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        }
        finally { session.BufferUpdated -= Check; }
    }
}
