using System.Windows.Automation;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实 WPF 对象页布局、只读预览、批量选择及空态绑定。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageViewTests
{
    private readonly WpfTestFixture _fixture;
    /// <summary>复用真实资源与独立 Dispatcher。</summary>
    public ObjectStorageViewTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>固定宽度详情保留可用空间，预览使用只读文本，下载按钮落在视口内。</summary>
    [Fact]
    public Task LoadedObjects_PreviewIsReadOnlyAndActionsFitViewport() => _fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage(
                [new("docs/", true), new("config.json", false, 2048), new("archive.zip", false, 1024)])),
            DetailsHandler = (_, _, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, 2048, null,
                "Standard", "application/json", "继承 Bucket（私有）", "<script>alert('literal only')</script>"))
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, editor: new ObjectStorageTestInstanceEditor());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var rows = Assert.Single(WindowTestVisuals.Descendants<ListBox>(view));
            rows.SelectedItem = model.Entries.Single(entry => entry.Key == "config.json");
            await WpfTestFixture.DrainDispatcherAsync();
            var preview = Assert.Single(WindowTestVisuals.Descendants<TextBox>(view), box => AutomationProperties.GetName(box) == "对象只读文本预览");
            Assert.True(preview.IsReadOnly);
            Assert.Equal("<script>alert('literal only')</script>", preview.Text);
            Assert.True(preview.IsVisible);
            var details = Assert.Single(WindowTestVisuals.Descendants<ObjectStorageDetailsView>(view));
            Assert.InRange(details.ActualWidth, 277, 279);
            var download = Assert.Single(WindowTestVisuals.Descendants<Button>(details), button => ReferenceEquals(button.Command, model.DownloadCommand));
            Assert.True(download.IsVisible);
            AssertContained(WindowTestVisuals.ScreenBounds(view), WindowTestVisuals.ScreenBounds(download));
            var search = Assert.Single(WindowTestVisuals.Descendants<TextBox>(view), box => AutomationProperties.GetName(box) == "搜索当前目录文件和文件夹名称，忽略大小写；支持 * 和 ? 通配符，例如 *.png");
            Assert.Equal(32, search.ActualHeight);
            AssertContained(WindowTestVisuals.ScreenBounds(view), WindowTestVisuals.ScreenBounds(search));
            var help = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => AutomationProperties.GetName(button) == Text.Get("ObjectSearchHelp"));
            AssertContained(WindowTestVisuals.ScreenBounds(view), WindowTestVisuals.ScreenBounds(help));
            var tooltip = Assert.IsType<ToolTip>(help.ToolTip);
            Assert.Equal(Text.Get("ObjectSearchSyntax"), Assert.IsType<TextBlock>(tooltip.Content).Text);
            help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(tooltip.IsOpen);
            Assert.Same(help, tooltip.PlacementTarget);
            tooltip.IsOpen = false;
            var checkbox = Assert.Single(WindowTestVisuals.Descendants<CheckBox>(rows), box => AutomationProperties.GetName(box) == "勾选 config.json");
            checkbox.IsChecked = true;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(1, model.CheckedCount);
            var batch = Assert.Single(WindowTestVisuals.Descendants<Button>(view), button => ReferenceEquals(button.Command, model.BatchDownloadCommand));
            Assert.True(batch.IsVisible);
            Assert.True(batch.IsEnabled);
            Assert.Equal("config.json", model.SelectedEntry?.Key);
        }
        finally { window.Close(); }
    });

    /// <summary>最小窗口收窄搜索和次要列，恢复设计宽度后完整元数据再次可见。</summary>
    [Fact]
    public Task NarrowViewport_PreservesNamesSearchAndRowActions() => _fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("config.json", false, 2048)]))
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        window.Width = 760;
        window.Height = 620;
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var list = Assert.Single(WindowTestVisuals.Descendants<ObjectStorageListView>(view));
            var search = Assert.Single(WindowTestVisuals.Descendants<TextBox>(list));
            Assert.InRange(search.ActualWidth, 80, 144);
            Assert.False(list.ShowSearchDecorations);
            Assert.Equal(Visibility.Collapsed, list.StorageColumnVisibility);
            Assert.Equal(Visibility.Collapsed, list.ModifiedColumnVisibility);
            var rows = Assert.Single(WindowTestVisuals.Descendants<ListBox>(list));
            var name = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(rows), text => text.Text == "config.json");
            Assert.True(name.ActualWidth >= 100, $"File name width={name.ActualWidth}");
            foreach (var button in WindowTestVisuals.Descendants<Button>(list).Where(button => button.IsVisible))
                AssertContained(WindowTestVisuals.ScreenBounds(list), WindowTestVisuals.ScreenBounds(button));
            AssertContained(WindowTestVisuals.ScreenBounds(list), WindowTestVisuals.ScreenBounds(search));

            window.Width = 1120;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(Visibility.Visible, list.StorageColumnVisibility);
            Assert.Equal(Visibility.Visible, list.ModifiedColumnVisibility);
            Assert.InRange(search.ActualWidth, 219, 221);
            Assert.True(list.ShowSearchDecorations);
        }
        finally { window.Close(); }
    });

    /// <summary>空目录不显示假数据，失败与无桶状态始终有可见恢复入口。</summary>
    [Theory]
    [InlineData("empty", "这个目录还没有对象")]
    [InlineData("failure", "无法读取 Bucket")]
    [InlineData("no-buckets", "没有可访问的 Bucket")]
    public Task BoundaryState_ShowsCorrectRecoveryOrUpload(string state, string title) => _fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService();
        if (state == "failure") service.ListObjectsHandler = (_, _, _, _, _, _) => throw new ObjectStorageException("请检查读取权限", "AccessDenied");
        if (state == "no-buckets") service.Buckets = [];
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, editor: new ObjectStorageTestInstanceEditor());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Empty(model.Entries);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), text => text.IsVisible && text.Text == (state == "failure" ? Text.Get("WorkspaceConnectionFailed") : title));
            var upload = WindowTestVisuals.Descendants<Button>(view).SingleOrDefault(button => button.Content is string label && label == "上传文件" && button.IsVisible);
            if (state == "empty") Assert.NotNull(upload);
            else
            {
                Assert.Null(upload);
                Assert.Contains(WindowTestVisuals.Descendants<Button>(view), button => button.IsVisible && button.Content is string label && label == "重试");
            }
        }
        finally { window.Close(); }
    });

    /// <summary>测试宿主真实显示且不激活，尺寸对应独立的对象浏览器组件。</summary>
    private static HermodWindow CreateWindow(ObjectStorageView view) => new()
    { Content = view, Width = 1120, Height = 820, Title = "OSS 测试", Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
    /// <summary>所有操作必须落在内容区内，避免固定详情底栏向外溢出。</summary>
    private static void AssertContained(Rect outer, Rect inner) => Assert.True(inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1
        && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1, $"Outer={outer}; Inner={inner}");
}
