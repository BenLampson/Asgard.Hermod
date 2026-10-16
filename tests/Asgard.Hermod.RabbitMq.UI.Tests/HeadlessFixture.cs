namespace Asgard.Hermod.RabbitMq.UI.Tests;

/// <summary>只启动测试 Dispatcher 和资源字典，不构造 Window、不调用 App.Startup。</summary>
public sealed class HeadlessFixture : IAsyncLifetime
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private Application? _application;
    public HeadlessFixture()
    {
        _thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string name in new[] { "Theme.Light", "Fonts", "Styles" }) _application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Asgard.Hermod.UI;component/Resources/{name}.xaml") });
                _ready.SetResult(dispatcher); Dispatcher.Run();
            }
            catch (Exception error) { _ready.TrySetException(error); }
        })
        { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
    }
    public async ValueTask InitializeAsync() => await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    public async Task RunAsync(Func<Task> action)
    {
        var dispatcher = await _ready.Task;
        await dispatcher.InvokeAsync(async () =>
        {
            Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
            try { Assert.Empty(_application!.Windows.Cast<Window>()); await action(); Assert.Empty(_application.Windows.Cast<Window>()); }
            finally { Text.SetApplicationCulture(null); }
        }).Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        var dispatcher = await _ready.Task;
        await dispatcher.InvokeAsync(() => { _application?.Shutdown(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); });
        Assert.True(_thread.Join(TimeSpan.FromSeconds(10)));
    }
}
