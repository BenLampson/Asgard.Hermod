namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>在独立 STA 线程执行 WPF 测试，并将异常交还测试运行器。</summary>
internal static class WpfTestThread
{
    /// <summary>加载 Windows 自带英文布局，不改变用户偏好或广播全局输入法切换。</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint LoadKeyboardLayout(string layout, uint flags);

    /// <summary>仅激活当前测试线程的固定布局。</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint ActivateKeyboardLayout(nint layout, uint flags);

    /// <summary>避免测试线程与 WPF 的 Dispatcher 线程要求冲突。</summary>
    internal static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 用例以固定键盘布局验证协议字节；只调整当前测试线程，不继承开发机的第三方输入法状态。
                // 中文桌面可能尚未加载英文布局；仅加载并激活当前测试线程，不广播 Shell 通知。
                nint layout = LoadKeyboardLayout("00000409", 0x80);
                Assert.NotEqual(nint.Zero, layout);
                Assert.NotEqual(nint.Zero, ActivateKeyboardLayout(layout, 0));
                action();
            }
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
