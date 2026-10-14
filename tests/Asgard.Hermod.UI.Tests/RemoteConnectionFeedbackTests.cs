using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Views;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>覆盖 SSH 与无持久 TCP 会话的存储初连、失效和取消，使用真实浮层布局且不访问外部服务。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RemoteConnectionFeedbackTests(WpfTestFixture fixture)
{
    [Fact]
    public Task Ssh_ConnectingFailureAndDisconnectionUseOverlayWithStableBody() => fixture.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<ISshConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new SshTestFactory { Handler = _ => pending.Task };
        await using var model = SshWorkspaceTests.Create(factory);
        var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1000, Height = 720 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout")); var size = layout.RenderSize;
            var opening = model.InitializeAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.Connection.IsConnecting); Assert.True(layout.IsOverlayOpen); Assert.Equal(size, layout.RenderSize);
            await WindowPreviewCapture.VerifyAsync(window, "ssh-common-connection-loading");
            var connection = new SshTestConnection(); pending.SetResult(connection); await opening;
            connection.Output("history"); await SshWorkspaceTests.UntilAsync(() => model.Session!.GetVisibleText().Contains("history", StringComparison.Ordinal));
            connection.End(); await SshWorkspaceTests.UntilAsync(() => model.Connection.IsDisconnected);
            Assert.True(model.Connection.IsOpen); Assert.Contains("history", model.Session!.GetVisibleText());
            await WindowTestVisuals.UpdateLayoutAsync(window); Assert.Equal(size, layout.RenderSize);
            await WindowPreviewCapture.VerifyAsync(window, "ssh-common-connection-interrupted");
            model.Connection.DismissCommand.Execute(null);
            factory.Handler = _ => throw new IOException("password=hidden-secret"); await model.ReconnectCommand.ExecuteAsync();
            Assert.True(model.Connection.HasError); Assert.DoesNotContain("hidden-secret", model.Connection.Error);
            Assert.Contains("history", model.Session!.GetVisibleText());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Storage_InitialWaitCancelAndSafeFailureStayInOverlay() => fixture.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ObjectStorageBucket>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ObjectStorageTestService { ListBucketsHandler = (_, _) => pending.Task };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, editor: new ObjectStorageTestInstanceEditor());
        var view = new ObjectStorageView { DataContext = model }; var window = new HermodWindow { Content = view, Width = 1080, Height = 760 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var body = Assert.IsType<Grid>(view.FindName("BrowserSurface")); var size = body.RenderSize;
            var opening = model.InitializeAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.Connection.IsConnecting); Assert.False(body.IsEnabled); Assert.Equal(size, body.RenderSize);
            await WindowPreviewCapture.VerifyAsync(window, "storage-common-connection-loading");
            model.Connection.CancelCommand.Execute(null); pending.SetResult(service.Buckets); await opening;
            Assert.False(model.Connection.IsOpen); Assert.False(model.IsInitialized); Assert.Empty(model.Buckets);
            service.ListBucketsHandler = (_, _) => throw ObjectStorageException.FromResource("AuthenticationFailedEditTheInstanceConnectionCredentials", "AuthenticationFailed");
            await model.ReconnectCommand.ExecuteAsync(); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(model.Connection.HasError); Assert.Equal("", model.DisplayReadError); Assert.False(model.ShowState); Assert.Equal(size, body.RenderSize);
            await WindowPreviewCapture.VerifyAsync(window, "storage-common-connection-failed");
            model.Connection.DismissCommand.Execute(null); Assert.False(model.Connection.IsOpen);
            service.ListBucketsHandler = null; await model.ReconnectCommand.ExecuteAsync(); Assert.False(model.Connection.HasError); Assert.True(model.IsInitialized);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("AuthenticationFailed", true)]
    [InlineData("ServiceUnavailable", true)]
    [InlineData("AccessDenied", false)]
    public Task Storage_RequestFailurePreservesRowsAndOnlyConnectionErrorsUsePopup(string code, bool disconnected) => fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService { ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("kept.txt", false, 42)])) };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync();
        service.ListObjectsHandler = (_, _, _, _, _, _) => throw ObjectStorageException.FromResource("AuthenticationFailedEditTheInstanceConnectionCredentials", code);
        await model.RefreshAsync(); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal("kept.txt", Assert.Single(model.Entries).Key); Assert.Equal(disconnected, model.Connection.IsDisconnected);
        Assert.Equal(disconnected, model.Connection.IsOpen);
        if (disconnected)
        {
            Assert.Equal(Text.Get("WorkspaceConnectionInterrupted"), model.Connection.Title); Assert.Equal("", model.DisplayReadError);
            model.Connection.DismissCommand.Execute(null); await model.RefreshAsync(); await WpfTestFixture.DrainDispatcherAsync(); Assert.False(model.Connection.IsOpen);
        }
        else Assert.NotEmpty(model.DisplayReadError);
    });
}
