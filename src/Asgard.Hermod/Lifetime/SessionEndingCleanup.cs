using System.Windows.Threading;

namespace Asgard.Hermod.Lifetime;

/// <summary>WPF 会话结束事件是同步协议，以有界嵌套消息循环等待异步释放且不否决 Windows 请求。</summary>
public static class SessionEndingCleanup
{
    /// <summary>Windows 的会话结束请求必须继续；Cancel 仅表示真正否决，不作为异步暂停。</summary>
    public static bool Handle(System.ComponentModel.CancelEventArgs args, Func<Task> cleanup, TimeSpan budget)
    {
        args.Cancel = false;
        return Run(cleanup, budget);
    }
    /// <summary>清理在预算内完成返回真；超时仍让系统继续会话结束，不发起实际注销命令。</summary>
    public static bool Run(Func<Task> cleanup, TimeSpan budget)
    {
        Task task = cleanup();
        if (task.IsCompleted) { task.GetAwaiter().GetResult(); return true; }
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = budget };
        timer.Tick += (_, _) => frame.Continue = false;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try { timer.Start(); Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (task.IsCompleted) { task.GetAwaiter().GetResult(); return true; }
        return false;
    }
}
