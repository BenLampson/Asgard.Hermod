namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>在独立 STA 线程执行 WPF 测试，并将异常交还测试运行器。</summary>
internal static class WpfTestThread
{
    /// <summary>避免测试线程与 WPF 的 Dispatcher 线程要求冲突。</summary>
    internal static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF test did not finish.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
