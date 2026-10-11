namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实详情视图绑定，不将未接入详情的实例显示为服务配置失败。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ServiceDetailViewTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>复用独立 STA 上的真实 WPF 资源和消息循环。</summary>
    public ServiceDetailViewTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>两种无详情状态显示对应说明，并隐藏重启部署及旧指标面板。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WithoutDetails_ShowsPlaceholderAndHidesServiceActions(bool configured) => _fixture.RunAsync(async () =>
    {
        var summary = new InstanceSummary("instance", "instance", "测试实例", "database", [],
            TypeId: configured ? "storage" : "", ProviderId: configured ? "aliyun-oss" : "");
        var model = new ServiceDetailViewModel(new InstanceItemViewModel(summary), snapshot: null);
        var view = new ServiceDetailView { DataContext = model };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);

            var buttons = WindowTestVisuals.Descendants<Button>(view).ToArray();
            var restart = Assert.Single(buttons, button => ReferenceEquals(button.Command, model.RestartCommand));
            var deploy = Assert.Single(buttons, button => ReferenceEquals(button.Command, model.DeployCommand));
            Assert.Equal(Visibility.Collapsed, restart.Visibility);
            Assert.Equal(Visibility.Collapsed, deploy.Visibility);
            Assert.False(Assert.Single(WindowTestVisuals.Descendants<ConfigurationPanel>(view)).IsVisible);
            Assert.False(Assert.Single(WindowTestVisuals.Descendants<EventsPanel>(view)).IsVisible);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), text => text.IsVisible
                && text.Text == (configured ? "实例已配置" : "No connector configured"));
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), text => text.IsVisible
                && text.Text == model.PlaceholderDescription);
            var badge = Assert.Single(WindowTestVisuals.Descendants<StatusBadge>(view));
            Assert.Equal(configured ? "已配置" : "Not configured", badge.Text);
            Assert.Equal(StatusTone.Muted, badge.Tone);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>设计快照仍呈现已有指标、配置与事件，旧操作保持可见但不可执行。</summary>
    [Fact]
    public Task ProvidedSnapshot_ShowsPreviewDetailsAndKeepsActionsDisabled() => _fixture.RunAsync(async () =>
    {
        var snapshot = new ServiceSnapshot("Demo service", "Design preview", "Running", StatusTone.Success, true,
            [new("CPU", "23%", "cpu")], [new("Host", "demo.local")],
            [new("Preview service created", "刚刚", "check", StatusTone.Success)]);
        var model = new ServiceDetailViewModel(
            new InstanceItemViewModel(new InstanceSummary("demo", "demo", "预览", "server", [])), snapshot);
        var view = new ServiceDetailView { DataContext = model };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);

            var buttons = WindowTestVisuals.Descendants<Button>(view).ToArray();
            var restart = Assert.Single(buttons, button => ReferenceEquals(button.Command, model.RestartCommand));
            var deploy = Assert.Single(buttons, button => ReferenceEquals(button.Command, model.DeployCommand));
            Assert.True(restart.IsVisible);
            Assert.True(deploy.IsVisible);
            Assert.False(restart.IsEnabled);
            Assert.False(deploy.IsEnabled);
            Assert.True(Assert.Single(WindowTestVisuals.Descendants<ConfigurationPanel>(view)).IsVisible);
            Assert.True(Assert.Single(WindowTestVisuals.Descendants<EventsPanel>(view)).IsVisible);
            var textBlocks = WindowTestVisuals.Descendants<TextBlock>(view).ToArray();
            Assert.Contains(textBlocks, text => text.IsVisible && text.Text == "23%");
            Assert.Contains(textBlocks, text => text.IsVisible && text.Text == "demo.local");
            Assert.Contains(textBlocks, text => text.IsVisible && text.Text == "Preview service created");
            Assert.False(Assert.Single(textBlocks, text => text.Text == model.PlaceholderTitle).IsVisible);
            var badge = Assert.Single(WindowTestVisuals.Descendants<StatusBadge>(view));
            Assert.Equal("Running", badge.Text);
            Assert.Equal("Sample data · service is not connected.", badge.ToolTip);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>真实显示但不激活测试宿主，允许绑定及显示树完成初始化。</summary>
    private static Window CreateWindow(ServiceDetailView view) => new()
    {
        Content = view,
        Width = 1000,
        Height = 720,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual
    };
}
