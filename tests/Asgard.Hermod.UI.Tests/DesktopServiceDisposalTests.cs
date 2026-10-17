namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 DI 在异步释放后可能切换线程，窗口服务仍必须回到原 STA 线程收尾。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DesktopServiceDisposalTests(WpfTestFixture fixture)
{
    /// <summary>释放前既有可见窗口也有隐藏窗口，异步服务先完成且不恢复任何已关闭入口。</summary>
    [Fact]
    public Task AsyncContainerDisposalClosesChatAndSettingsOnDispatcher() => fixture.RunAsync(async () =>
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IAgentWindowService, AgentWindowService>();
        registrations.AddSingleton(_ => new SettingsEditorService(() => null, new TagManagementTestService(), new Languages()));
        registrations.AddSingleton<AsyncBoundary>();
        var services = registrations.BuildServiceProvider();
        var agents = services.GetRequiredService<IAgentWindowService>();
        var settings = services.GetRequiredService<SettingsEditorService>();
        var boundary = services.GetRequiredService<AsyncBoundary>();
        agents.Open(new(new object(), new object(), "Test", "", "terminal", () => new Border(), () => { }, () => true));
        var visible = Assert.Single(Application.Current.Windows.OfType<AgentChatWindow>());
        agents.Open(new(new object(), new object(), "Test hidden", "", "terminal", () => new Border(), () => { }, () => true));
        var hidden = Application.Current.Windows.OfType<AgentChatWindow>().Single(window => window != visible);
        hidden.Close();
        await settings.OpenAsync();
        Task disposing = services.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        boundary.Complete();
        await disposing;
        Assert.True(boundary.Finished);
        Assert.Empty(Application.Current.Windows.Cast<Window>());
        agents.Open(new(new object(), new object(), "Late", "", "terminal", () => new Border(), () => { }, () => true));
        await settings.OpenAsync();
        Assert.Empty(Application.Current.Windows.Cast<Window>());
    });

    private sealed class AsyncBoundary : IAsyncDisposable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Finished;
        internal void Complete() => _completion.TrySetResult();
        public async ValueTask DisposeAsync() { await _completion.Task.ConfigureAwait(false); Finished = true; }
    }

    private sealed class Languages : ILanguagePreferenceStore
    {
        public string? Load() => "en";
        public void Save(string? language) { }
    }
}
