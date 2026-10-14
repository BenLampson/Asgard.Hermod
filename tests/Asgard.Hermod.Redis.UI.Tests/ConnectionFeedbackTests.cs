using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>Redis 连接失败在覆盖层显示，短徽标不重复错误或误用绿色。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ConnectionFeedbackTests(WpfTestFixture fixture)
{
    /// <summary>连接后的数据库计数仍可取消，不保留半初始化会话或迟到计数。</summary>
    [Fact]
    public Task CancelDuringCounts_ReleasesSessionAndIgnoresLateData() => fixture.RunAsync(async () =>
    {
        var count = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new RedisTestSession { DatabaseKeyCount = _ => count.Task }; var configs = new RedisTestConfigurations();
        await using var model = new RedisWorkspaceViewModel(new(RedisTestConfigurations.Instance), configs, session, configs, new RedisTestDialogs());
        var pending = model.InitializeAsync(TestContext.Current.CancellationToken); Assert.True(model.Connection.IsConnecting);
        model.Connection.CancelCommand.Execute(null); await pending;
        Assert.False(model.IsConnected); Assert.False(model.Connection.IsOpen); Assert.Equal(1, session.Disposals); Assert.Empty(model.Databases);
        count.SetResult(42); await WpfTestFixture.DrainDispatcherAsync(); Assert.Empty(model.Databases); Assert.False(model.Connection.HasError);
    });
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Connect_CancelLateSessionAndRetryFailure_KeepBodyBounds(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var session = new RedisTestSession { BlockedOpen = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var configs = new RedisTestConfigurations();
        await using var model = new RedisWorkspaceViewModel(new(RedisTestConfigurations.Instance), configs, session, configs, new RedisTestDialogs());
        var view = new RedisWorkspaceView { DataContext = model }; var window = new HermodWindow { Width = 1100, Height = 760, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout")); var body = Assert.IsType<Grid>(view.FindName("Body")); var size = body.RenderSize;
            var pending = model.InitializeAsync(TestContext.Current.CancellationToken); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(layout.IsOverlayOpen); Assert.True(model.Connection.IsConnecting); Assert.False(body.IsEnabled); Assert.Equal(size, body.RenderSize);
            await WindowPreviewCapture.VerifyAsync(window, $"redis-connecting-{language}-{theme}");
            model.Connection.CancelCommand.Execute(null); Assert.True(model.Connection.IsCancelling);
            session.BlockedOpen.SetResult(); await pending; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(1, session.Disposals); Assert.False(model.IsConnected); Assert.False(layout.IsOverlayOpen); Assert.Equal(size, body.RenderSize);
            session.BlockedOpen = null; session.OpenError = new IOException("password=hidden-test-secret");
            await model.ReconnectCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.Connection.HasError); Assert.True(layout.IsOverlayOpen); Assert.False(model.HasError); Assert.Equal(size, body.RenderSize);
            Assert.DoesNotContain("hidden-test-secret", model.Connection.Error); Assert.Equal(Text.Get("WorkspaceConnectionDisconnected"), model.Status);
            Assert.Equal(StatusTone.Danger, Assert.IsType<StatusBadge>(view.FindName("ConnectionBadge")).Tone);
            await WindowPreviewCapture.VerifyAsync(window, $"redis-connection-failed-{language}-{theme}");
            window.Width = 620; window.Height = 640; await WindowPreviewCapture.VerifyAsync(window, $"redis-connection-failed-narrow-{language}-{theme}");
            var overlay = Assert.IsType<WorkspaceConnectionOverlay>(layout.Overlay); var card = Assert.IsType<Border>(overlay.FindName("ConnectionCard"));
            Assert.InRange(card.ActualWidth, 300, 440); Assert.Same(model.ReconnectCommand, Assert.IsType<Button>(overlay.FindName("RetryConnection")).Command);
            model.Connection.DismissCommand.Execute(null); await WindowTestVisuals.UpdateLayoutAsync(window); Assert.False(layout.IsOverlayOpen); Assert.True(body.IsEnabled);
            session.OpenError = null; await model.ReconnectCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.IsConnected); Assert.False(model.Connection.HasError); Assert.False(layout.IsOverlayOpen);
            Assert.Equal(StatusTone.Success, Assert.IsType<StatusBadge>(view.FindName("ConnectionBadge")).Tone);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });
}
