using System.Windows.Automation;

namespace Asgard.Hermod.UI.Tests;

/// <summary>从列表与详情的实际按钮打开菜单，验证对象目标、危险颜色和完整的显示布局。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageMenuTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>复用真实主题、菜单模板和 STA Dispatcher。</summary>
    public ObjectStorageMenuTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>两个入口在两种主题下都操作本次对象，删除图标与标题保持一致且不裁切。</summary>
    [Theory]
    [InlineData(UiTheme.Light, false)]
    [InlineData(UiTheme.Dark, false)]
    [InlineData(UiTheme.Light, true)]
    [InlineData(UiTheme.Dark, true)]
    public Task FileMenu_UsesCurrentObjectAndKeepsLabelsVisible(UiTheme theme, bool details) => _fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var previews = new ContextMenuPreviewScope();
        var dialogs = new ObjectStorageTestDialogs();
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(CreateService(), dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        ContextMenu? menu = null;
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var row = model.Entries.Single(entry => entry.Key == "config.json");
            await model.SelectEntryAsync(details ? row : model.Entries.Single(entry => entry.Key == "other.txt"), TestContext.Current.CancellationToken);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(view), item =>
                AutomationProperties.GetName(item) == (details ? "当前对象更多操作" : "config.json 更多操作"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            menu = Assert.IsType<ContextMenu>(button.ContextMenu);
            Assert.True(menu.IsOpen);
            menu.UpdateLayout();
            Assert.Same(row, model.SelectedEntry);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.All(items, item => Assert.Same(row, item.CommandParameter));
            Assert.Equal(!details, items.Any(item => ReferenceEquals(item.Command, model.DownloadCommand)));
            Assert.All(items, AssertLabelFits);
            var delete = items.Single(item => ReferenceEquals(item.Command, model.DeleteCommand));
            var danger = Assert.IsType<SolidColorBrush>(Application.Current.FindResource("DangerBrush")).Color;
            Assert.Equal(danger, Assert.IsType<SolidColorBrush>(Label(delete).Foreground).Color);
            Assert.Equal(danger, Assert.IsType<SolidColorBrush>(Assert.IsType<IconView>(delete.Icon).Foreground).Color);
            var link = items.Single(item => ReferenceEquals(item.Command, model.TemporaryLinkCommand));
            Assert.True(link.IsEnabled);
            ContextMenuPreviewScope.Save(menu, $"object-storage-menu-{(details ? "details" : "list")}-{theme}");

            await ContextMenuPreviewScope.InvokeAsync(items.Single(item => ReferenceEquals(item.Command, model.CopyKeyCommand)));
            Assert.Equal("config.json", Assert.Single(dialogs.CopiedTexts));
            Assert.False(menu.IsOpen);
        }
        finally
        {
            if (menu is not null) menu.IsOpen = false;
            window.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>更换模板后目录仍无法生成临时对象链接，不以样式变化放开不支持的操作。</summary>
    [Fact]
    public Task DirectoryMenu_KeepsTemporaryLinkDisabled() => _fixture.RunAsync(async () =>
    {
        using var previews = new ContextMenuPreviewScope();
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(CreateService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        ContextMenu? menu = null;
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(view), item => AutomationProperties.GetName(item) == "docs/ 更多操作");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            menu = Assert.IsType<ContextMenu>(button.ContextMenu);
            Assert.True(menu.IsOpen);
            var link = Assert.Single(menu.Items.OfType<MenuItem>(), item => ReferenceEquals(item.Command, model.TemporaryLinkCommand));
            Assert.False(link.IsEnabled);
            Assert.False(link.Command.CanExecute(link.CommandParameter));
            ContextMenuPreviewScope.Save(menu, "object-storage-menu-directory-Light");
        }
        finally
        {
            if (menu is not null) menu.IsOpen = false;
            window.Close();
        }
    });

    /// <summary>获取模板生成的实际文字，检查标题没有落入默认控件的颜色与图标栏布局。</summary>
    private static TextBlock Label(MenuItem item) => Assert.Single(WindowTestVisuals.Descendants<TextBlock>(
        Assert.IsType<ContentPresenter>(item.Template.FindName("HeaderPresenter", item))));

    /// <summary>验证图标与文字具有真实尺寸，标题未被菜单边界截断。</summary>
    private static void AssertLabelFits(MenuItem item)
    {
        var label = Label(item);
        var origin = label.TranslatePoint(new Point(), item);
        Assert.True(label.ActualWidth >= label.DesiredSize.Width - 1);
        Assert.True(origin.X + label.ActualWidth <= item.ActualWidth);
        Assert.True(origin.Y >= 0 && origin.Y + label.ActualHeight <= item.ActualHeight);
        Assert.True(Assert.IsType<IconView>(item.Icon).ActualWidth > 0);
    }

    /// <summary>测试列表只使用本地替身，包含目录及两个可操作对象。</summary>
    private static ObjectStorageTestService CreateService() => new()
    {
        ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage(
            [new("docs/", true), new("config.json", false, 2048), new("other.txt", false, 1024)]))
    };

    /// <summary>保持设计组件宽度，让实际更多按钮与右侧详情同时完成布局。</summary>
    private static HermodWindow CreateWindow(ObjectStorageView view) => new() { Content = view, Width = 1120, Height = 820 };
}
