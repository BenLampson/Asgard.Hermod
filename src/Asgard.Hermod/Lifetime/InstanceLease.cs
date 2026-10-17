namespace Asgard.Hermod.Lifetime;

/// <summary>专用线程持有命名互斥量，异步启动及释放不受线程所有权限制。</summary>
internal sealed class InstanceLease : IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;
    internal bool Acquired { get; }

    /// <summary>内核原子竞争且限制到当前用户、当前登录会话，崩溃后允许取得弃置锁。</summary>
    internal InstanceLease(string name)
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(false, name, new NamedWaitHandleOptions
                { CurrentSessionOnly = true, CurrentUserOnly = true });
                bool owned;
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
                ready.SetResult(owned);
                if (!owned) return;
                try { _release.Wait(); }
                finally { mutex.ReleaseMutex(); }
            }
            catch (Exception error) { ready.TrySetException(error); }
        })
        { IsBackground = true, Name = "Hermod instance ownership" };
        _thread.Start();
        Acquired = ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>先完成上层清理，再释放互斥量；返回后下一个进程可接管。</summary>
    public void Dispose()
    {
        _release.Set();
        _thread.Join();
        _release.Dispose();
    }
}
