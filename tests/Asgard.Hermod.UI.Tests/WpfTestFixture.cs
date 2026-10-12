namespace Asgard.Hermod.UI.Tests;

/// <summary>在独立 STA Dispatcher 执行用例，并加载真实程序集资源。</summary>
public sealed class WpfTestFixture : IAsyncLifetime
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> _unhandled = [];
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;
    private Application? _application;

    /// <summary>创建后台 STA 线程，测试逻辑不会使用测试运行器的线程上下文。</summary>
    public WpfTestFixture()
    {
        _thread = new Thread(RunDispatcher) { IsBackground = true, Name = "Hermod UI tests" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>等待应用和资源完成初始化。</summary>
    public async ValueTask InitializeAsync()
    {
        _dispatcher = await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>在 WPF 上下文执行异步用例，超时或窗口异步异常会使测试失败。</summary>
    public async Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Assert.NotNull(_dispatcher);
        var operation = _dispatcher.InvokeAsync(async () =>
        {
            int initialExceptions = _unhandled.Count;
            await action();
            Assert.Equal(initialExceptions, _unhandled.Count);
        });
        await operation.Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    }

    /// <summary>让已经排入 Dispatcher 的绑定、关闭和退出通知得到处理。</summary>
    public static async Task DrainDispatcherAsync()
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>应用资源与 Dispatcher 共用 STA 生命周期，结束后回收测试线程。</summary>
    private void RunDispatcher()
    {
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string name in new[] { "Theme.Light", "Fonts", "Styles" })
            {
                _application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Asgard.Hermod.UI;component/Resources/{name}.xaml")
                });
            }

            _application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Asgard.Hermod.ObjectStorage.UI;component/Resources/WorkspacePages.xaml")
            });
            _application.DispatcherUnhandledException += OnUnhandledException;
            _ready.TrySetResult(dispatcher);
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
        }
    }

    /// <summary>捕获窗口 async void 事件的异常，交还断言而不是中断整个运行器。</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        _unhandled.Add(args.Exception);
        args.Handled = true;
    }

    /// <summary>资源测试结束后关闭应用；不遗留 WPF 线程或窗口。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_dispatcher is not null && !_dispatcher.HasShutdownStarted)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _application?.Shutdown();
                // 此处运行的是独立 Dispatcher.Run，需要显式结束消息循环。
                _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            });
        }
        bool joined = await Task.Run(() => _thread.Join(TimeSpan.FromSeconds(10)), CancellationToken.None);
        Assert.True(joined, "WPF Dispatcher 没有完成关闭。");
    }
}
