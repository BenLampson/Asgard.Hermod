using System.Globalization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在真实传输列表验证只读进度绑定，覆盖上传、下载和双语显示。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageTransfersViewTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>复用真实主题资源和独立 STA 消息循环。</summary>
    public ObjectStorageTransfersViewTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>新增任务可以创建模板，服务进度持续更新控件，完成后隐藏进度条。</summary>
    [Theory]
    [InlineData("en", true)]
    [InlineData("en", false)]
    [InlineData("zh-Hans", true)]
    [InlineData("zh-Hans", false)]
    public Task AddedTransfer_RendersProgressAndCompletion(string language, bool upload) => _fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<ObjectStorageTransferProgress>? reportedProgress = null;
        using var transfer = new ObjectStorageTransferViewModel("reports/example.txt", upload, 100,
            (_, progress, token) =>
            {
                reportedProgress = progress;
                return complete.Task.WaitAsync(token);
            }, TestContext.Current.CancellationToken);
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(new ObjectStorageTestService());
        HermodWindow? window = null;
        Task? pending = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            model.IsTransferExpanded = true;
            var view = new ObjectStorageTransfersView { DataContext = model };
            window = new HermodWindow
            {
                Content = view,
                Width = 760,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);

            // 在窗口加载后新增任务，复现上传触发的 DataTemplate 实例化路径。
            model.Transfers.Add(transfer);
            pending = transfer.RunAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var bar = Assert.Single(WindowTestVisuals.Descendants<ProgressBar>(view));
            Assert.True(bar.IsVisible);
            Assert.Equal(0, bar.Value);

            Assert.NotNull(reportedProgress);
            reportedProgress.Report(new(40, 100));
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(40, bar.Value);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), block => block.Text == Text.Get("TransferTasks"));
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), block => block.Text == transfer.ProgressText);
            window.Width = 480;
            await WindowPreviewCapture.VerifyAsync(window, $"transfer-progress-{language}-{upload}");
            Assert.True(bar.ActualWidth > 0);
            Assert.True(bar.ActualWidth <= view.ActualWidth);

            complete.SetResult();
            await pending;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(100, bar.Value);
            Assert.False(bar.IsVisible);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), block => block.Text == Text.Get("Completed"));
        }
        finally
        {
            complete.TrySetResult();
            if (pending is not null) await pending;
            window?.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    });
}
