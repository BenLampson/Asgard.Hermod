namespace Asgard.Hermod.Lifetime;

/// <summary>退出提交后隔离清理故障，保留后续回收机会；诊断不记录异常消息中的凭据。</summary>
internal static class ExitCleanup
{
    /// <summary>同步清理失败只记录异常类型，不在已关闭的窗口上再弹出模态框。</summary>
    internal static void Run(Action cleanup)
    {
        try { cleanup(); }
        catch (Exception error) { Report(error); }
    }

    /// <summary>等待当前清理结束，失败后允许调用方继续执行其他回收步骤。</summary>
    internal static async Task RunAsync(Func<Task> cleanup)
    {
        try { await cleanup(); }
        catch (Exception error) { Report(error); }
    }

    /// <summary>只记录类型和调用栈，不输出 SDK 消息、连接参数或用户内容。</summary>
    private static void Report(Exception error) =>
        System.Diagnostics.Trace.TraceError("Hermod exit cleanup failed: {0}\n{1}", error.GetType().FullName, error.StackTrace);
}
