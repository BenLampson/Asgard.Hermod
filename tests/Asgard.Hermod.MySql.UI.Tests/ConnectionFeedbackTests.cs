using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>连接反馈按当前首次连接与内联重连约定验证，不创建窗口或发送桌面输入。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ConnectionFeedbackTests(WpfTestFixture fixture)
{
    /// <summary>已建立工作区的新查询连接失败在共享恢复状态中呈现，正文和文稿保持原位。</summary>
    [Fact]
    public Task QueryOpenFailureKeepsDocumentAndSafeSharedRecovery() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var configs = new Configurations();
        await using var model = Create(server, configs); await model.InitializeAsync(); var tab = Assert.Single(model.Tabs); tab.Sql = "SELECT 1";
        var view = new MySqlWorkspaceView { DataContext = model }; var root = Root(view); await Layout(root);
        var body = (Grid)view.FindName("Body"); var size = body.RenderSize;
        server.OpenError = new IOException("password=private-test-secret"); await tab.ExecuteAsync(tab.Sql); await Layout(root);
        Assert.True(tab.HasError); Assert.True(model.Connection.HasError); Assert.False(model.Connection.IsOpen);
        Assert.DoesNotContain("private-test-secret", model.Connection.Detail); Assert.Equal("SELECT 1", tab.Sql); Assert.Equal(size, body.RenderSize);
        var content = Assert.Single(WindowTestVisuals.Descendants<MySqlTabView>(view)); Assert.Same(model.Connection, WorkspaceConnectionScope.GetState(content));
        Assert.Equal(Visibility.Collapsed, ((Border)content.FindName("InlineError")).Visibility);
        model.Connection.DismissCommand.Execute(null); Assert.Equal("SELECT 1", tab.Sql); Assert.Equal(0, server.Executions); Assert.Equal(0, server.Writes);
    });
    /// <summary>已连接来源断开只显示内联恢复状态；旧代会话回调不会污染重连结果。</summary>
    [Fact]
    public Task QuerySessionDisconnectPreservesDraftAndNeverReopensPopup() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var configs = new Configurations();
        await using var model = Create(server, configs); await model.InitializeAsync(); var tab = Assert.Single(model.Tabs); tab.Sql = "SELECT 'retained draft';";
        await tab.ExecuteAsync(tab.Sql); var old = server.Sessions.Last(); await Task.Run(old.Disconnect); await WpfTestFixture.DrainDispatcherAsync();
        Assert.True(model.Connection.IsDisconnected); Assert.False(model.Connection.IsOpen); Assert.False(model.IsConnected); Assert.False(model.HasError); Assert.False(tab.CanRun);
        Assert.Equal("SELECT 'retained draft';", tab.Sql); Assert.Same(tab, model.ActiveTab); Assert.Equal(1, server.Executions); Assert.Equal(0, server.Writes);
        model.Connection.DismissCommand.Execute(null); old.Disconnect(); Assert.False(model.Connection.IsOpen);
        await model.ReconnectCommand.ExecuteAsync(); Assert.True(model.IsConnected); Assert.Equal("SELECT 'retained draft';", tab.Sql);
        old.Disconnect(); Assert.False(model.Connection.IsOpen); Assert.False(model.Connection.HasError); Assert.Equal(1, server.Executions);
    });
    /// <summary>初始连接、取消后的迟到会话、失败与显式重试不改变正文尺寸，也不依赖已移除的徽章。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task InitialCancelAndRetryPreserveBodyBounds(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var server = new TestMySql { BlockedOpen = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var configs = new Configurations();
            await using var model = Create(server, configs); var view = new MySqlWorkspaceView { DataContext = model }; var root = Root(view); await Layout(root);
            using var trace = new WpfBindingTrace(); var layout = (WorkspacePageLayout)view.FindName("WorkspaceLayout"); var body = (Grid)view.FindName("Body"); var size = body.RenderSize;
            var pending = model.InitializeAsync(); await Layout(root); Assert.True(layout.IsOverlayOpen); Assert.True(model.Connection.IsConnecting); Assert.False(body.IsEnabled); Assert.Equal(size, body.RenderSize);
            model.Connection.CancelCommand.Execute(null); Assert.True(model.Connection.IsCancelling); server.BlockedOpen.SetResult(); await pending; await Layout(root);
            Assert.Equal(1, server.Disposals); Assert.False(model.IsConnected); Assert.False(layout.IsOverlayOpen); Assert.Equal(size, body.RenderSize);
            server.BlockedOpen = null; server.OpenError = new IOException("password=hidden-test-secret"); await model.ReconnectCommand.ExecuteAsync(); await Layout(root);
            Assert.True(model.Connection.HasError); Assert.True(layout.IsOverlayOpen); Assert.False(model.HasError); Assert.Equal(size, body.RenderSize); Assert.DoesNotContain("hidden-test-secret", model.Connection.Detail);
            var overlay = Assert.IsType<WorkspaceConnectionOverlay>(layout.Overlay); Assert.Same(model.ReconnectCommand, ((Button)overlay.FindName("RetryConnection")).Command);
            model.Connection.DismissCommand.Execute(null); await Layout(root); Assert.False(layout.IsOverlayOpen); Assert.True(body.IsEnabled);
            server.OpenError = null; await model.ReconnectCommand.ExecuteAsync(); await Layout(root); Assert.True(model.IsConnected); Assert.True(model.HasConnectedOnce); Assert.False(model.Connection.HasError); Assert.Equal(size, body.RenderSize);
            Assert.Empty(trace.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });
    private static MySqlWorkspaceViewModel Create(TestMySql server, Configurations configs) => new(new(new("mysql-test", "MySQL", "MySQL", "database", [], TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct)), configs, server, configs, new TestMySqlDialogs(), "app");
    private static Border Root(UIElement view) { var root = new Border { Child = view }; root.Resources.MergedDictionaries.Add(Application.Current.Resources); return root; }
    private static async Task Layout(FrameworkElement root)
    {
        root.Measure(new(1100, 760)); root.Arrange(new(0, 0, 1100, 760)); root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
        Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
    }
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
