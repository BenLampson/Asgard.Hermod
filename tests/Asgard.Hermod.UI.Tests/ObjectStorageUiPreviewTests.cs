using System.Windows.Automation;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>用真实外壳核对纯实例侧栏与右侧对象浏览器在两套主题中的职责和布局。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageUiPreviewTests(WpfTestFixture fixture)
{
    /// <summary>从已配置实例进入页面，主要操作和对象详情均在设计尺寸的可视区域内。</summary>
    [Theory]
    [InlineData(UiTheme.Light, 1360, 860)]
    [InlineData(UiTheme.Dark, 1360, 860)]
    [InlineData(UiTheme.Light, 1000, 620)]
    [InlineData(UiTheme.Dark, 1000, 620)]
    public Task ConfiguredInstance_RendersBrowserInBothThemes(UiTheme theme, double width, double height) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var source = new ObjectStorageWorkspaceDataSource
        {
            Instance = new("oss-instance", "生产资源", "生产资源", "provider-aliyun-oss", ["Production"],
                Endpoint: "阿里云 OSS", TypeId: "storage", ProviderId: "aliyun-oss")
        };
        var service = CreateService();
        var editor = new FakeInstanceEditor();
        var factory = new InstanceWorkspaceFactory([new ObjectStoragePageProvider(service, new ObjectStorageTestDialogs(), editor)]);
        await using var shell = new ShellViewModel(source, editor, new FakeTerminalSessionFactory(), themes,
            pageFactory: factory);
        shell.Workspace.OpenInstance(shell.Sidebar.AllInstances[0]);
        var page = Assert.IsType<ObjectStorageWorkspaceViewModel>(shell.Workspace.SelectedTab);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        await page.NavigateAsync(service.Buckets[0], "releases/v0.9.0/", TestContext.Current.CancellationToken);
        await page.SelectEntryAsync(page.Entries.Single(entry => entry.Name == "config.json"), TestContext.Current.CancellationToken);
        var window = new Asgard.Hermod.MainWindow(shell) { Width = width, Height = height };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, $"object-storage-shell-{theme}-{width}");
            var view = Assert.Single(WindowTestVisuals.Descendants<ObjectStorageView>(window));
            var preview = Assert.Single(WindowTestVisuals.Descendants<TextBox>(view),
                box => AutomationProperties.GetName(box) == "对象只读文本预览");
            Assert.True(preview.IsVisible);
            Assert.Equal("已连接", page.Status);
            var sidebar = Assert.Single(WindowTestVisuals.Descendants<InstanceSidebarView>(window));
            var item = Assert.Single(shell.Sidebar.AllInstances);
            Assert.True(item.IsSelected);
            Assert.Equal(item.Id, page.InstanceId);
            Assert.Same(page, view.DataContext);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(sidebar), text => text.IsVisible
                && (text.Text == "BUCKETS" || service.Buckets.Any(bucket => bucket.Name == text.Text)
                    || text.Text == "releases" || text.Text == "v0.9.0"));
            var instanceButton = Assert.Single(WindowTestVisuals.Descendants<Button>(sidebar),
                button => button.IsVisible && ReferenceEquals(button.Command, shell.Sidebar.OpenInstanceCommand));
            Assert.Same(item, instanceButton.CommandParameter);
            Assert.Equal(34, instanceButton.ActualHeight);
            var instanceName = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(instanceButton), text => text.Text == item.Name);
            Assert.Equal(12, instanceName.FontSize);
            Assert.Equal(FontWeights.SemiBold, instanceName.FontWeight);
            Assert.Same(Application.Current.Resources["AccentTextBrush"], instanceName.Foreground);
            var instanceIcon = Assert.Single(WindowTestVisuals.Descendants<IconView>(instanceButton), icon => icon.Name == "InstanceTypeIcon");
            Assert.Equal("database", instanceIcon.Kind);
            var brand = Assert.Single(WindowTestVisuals.Descendants<IconView>(instanceButton), icon => icon.Name == "InstanceProviderIcon");
            Assert.Equal("provider-aliyun-oss", brand.Kind);
            Assert.True(brand.IsVisible);
            Assert.Equal(15, instanceIcon.ActualWidth);
            Assert.Same(Application.Current.Resources["WarningBrush"], instanceIcon.Foreground);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(view),
                text => text.IsVisible && text.Text == "No connector configured");
            var bounds = WindowTestVisuals.ScreenBounds(view);
            var persistentCommands = new object[] { page.UploadCommand, page.NewFolderCommand, page.RefreshCommand,
                page.LoadMoreCommand, page.DownloadCommand, page.CloseDetailsCommand };
            foreach (var button in WindowTestVisuals.Descendants<Button>(view).Where(button => button.IsVisible
                && (persistentCommands.Contains(button.Command)
                    || AutomationProperties.GetName(button) == "当前对象更多操作")))
            {
                var control = WindowTestVisuals.ScreenBounds(button);
                Assert.True(control.Left >= bounds.Left - 1 && control.Top >= bounds.Top - 1
                    && control.Right <= bounds.Right + 1 && control.Bottom <= bounds.Bottom + 1,
                    $"Theme={theme}; Content={bounds}; Button={control}");
            }
            // 元数据可在小窗口滚动，滚到末尾后仍可复制完整对象键。
            var details = Assert.Single(WindowTestVisuals.Descendants<ObjectStorageDetailsView>(view));
            var scroller = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(details), scroll => scroll.Content is StackPanel);
            scroller.ScrollToEnd();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var copyKey = Assert.Single(WindowTestVisuals.Descendants<Button>(details), button => ReferenceEquals(button.Command, page.CopyKeyCommand));
            var copyBounds = WindowTestVisuals.ScreenBounds(copyKey);
            Assert.True(copyBounds.Top >= bounds.Top && copyBounds.Bottom <= bounds.Bottom);

            // 收起导航仍使用同一实例命令，恢复后不重建或重新连接右侧浏览器。
            shell.IsSidebarExpanded = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var collapsedButton = Assert.Single(WindowTestVisuals.Descendants<Button>(sidebar),
                button => button.IsVisible && ReferenceEquals(button.Command, shell.Sidebar.OpenInstanceCommand));
            Assert.Equal(36, collapsedButton.ActualWidth);
            Assert.Same(item, collapsedButton.CommandParameter);
            var collapsedIcon = Assert.Single(WindowTestVisuals.Descendants<IconView>(collapsedButton), icon => icon.Kind == "database");
            Assert.Same(Application.Current.Resources["WarningBrush"], collapsedIcon.Foreground);
            WindowTestVisuals.Invoke(collapsedButton);
            Assert.Same(page, shell.Workspace.SelectedTab);
            Assert.Equal(1, service.BucketReads);
            shell.IsSidebarExpanded = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(item.IsSelected);
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>各层目录独立返回数据，截图不会依赖真实账号或网络。</summary>
    private static ObjectStorageTestService CreateService()
    {
        var modified = new DateTimeOffset(2026, 10, 12, 8, 40, 0, TimeSpan.FromHours(8));
        return new()
        {
            Buckets = [new("assets-prod", "cn-hangzhou"), new("backup-prod", "cn-shanghai")],
            ListObjectsHandler = (_, _, prefix, _, _, _) => Task.FromResult(new ObjectStoragePage(prefix switch
            {
                "" => [new("docs/", true), new("images/", true), new("releases/", true)],
                "releases/" => [new("releases/v0.9.0/", true)],
                "releases/v0.9.0/" => [new(prefix + "docs/", true), new(prefix + "images/", true),
                    new(prefix + "config.json", false, 2048, modified), new(prefix + "README.md", false, 12348, modified),
                    new(prefix + "app-v0.9.0.zip", false, 45793862, modified), new(prefix + "checksums.txt", false, 512, modified)],
                _ => []
            })),
            DetailsHandler = (_, _, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, 2048, modified,
                "Standard", "application/json", "继承 Bucket（私有）",
                "{\n  \"app\": \"Hermod\",\n  \"version\": \"0.9.0\",\n  \"environment\": \"production\",\n  \"features\": {\n    \"storage\": true\n  }\n}"))
        };
    }
}
