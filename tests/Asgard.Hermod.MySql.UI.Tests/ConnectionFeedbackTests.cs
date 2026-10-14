using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>连接等待、取消、失败与重试使用同一浮层，不挤压数据库正文。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ConnectionFeedbackTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Connect_CancelLateSessionAndRetryFailure_KeepBodyBounds(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var server = new TestMySql { BlockedOpen = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var configs = new Configurations();
        await using var model = new MySqlWorkspaceViewModel(new(new("mysql-test", "MySQL", "MySQL", "database", [], TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct)), configs, server, configs, new TestMySqlDialogs());
        var view = new MySqlWorkspaceView { DataContext = model }; var window = new HermodWindow { Width = 1100, Height = 760, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout")); var body = Assert.IsType<Grid>(view.FindName("Body")); var size = body.RenderSize;
            var pending = model.InitializeAsync(TestContext.Current.CancellationToken); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(layout.IsOverlayOpen); Assert.True(model.Connection.IsConnecting); Assert.False(body.IsEnabled); Assert.Equal(size, body.RenderSize);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-connecting-{language}-{theme}");
            model.Connection.CancelCommand.Execute(null); Assert.True(model.Connection.IsCancelling);
            server.BlockedOpen.SetResult(); await pending; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(1, server.Disposals); Assert.False(model.IsConnected); Assert.False(layout.IsOverlayOpen); Assert.Equal(size, body.RenderSize);
            server.BlockedOpen = null; server.OpenError = new IOException("password=hidden-test-secret");
            await model.ReconnectCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.Connection.HasError); Assert.True(layout.IsOverlayOpen); Assert.False(model.HasError); Assert.Equal(size, body.RenderSize);
            Assert.DoesNotContain("hidden-test-secret", model.Connection.Error); Assert.Equal(StatusTone.Danger, Assert.IsType<StatusBadge>(view.FindName("Badge")).Tone);
            var overlay = Assert.IsType<WorkspaceConnectionOverlay>(layout.Overlay); Assert.Same(model.ReconnectCommand, Assert.IsType<Button>(overlay.FindName("RetryConnection")).Command);
            await WindowPreviewCapture.VerifyAsync(window, $"mysql-connection-failed-{language}-{theme}");
            model.Connection.DismissCommand.Execute(null); await WindowTestVisuals.UpdateLayoutAsync(window); Assert.False(layout.IsOverlayOpen); Assert.True(body.IsEnabled);
            server.OpenError = null; await model.ReconnectCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.IsConnected); Assert.False(model.Connection.HasError); Assert.False(layout.IsOverlayOpen); Assert.Equal(size, body.RenderSize);
            Assert.Equal(StatusTone.Success, Assert.IsType<StatusBadge>(view.FindName("Badge")).Tone);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
