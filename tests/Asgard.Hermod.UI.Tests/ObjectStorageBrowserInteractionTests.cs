using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证右侧 Bucket、目录和对象交互，确保选择入口之间不串联错误操作。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageBrowserInteractionTests(WpfTestFixture fixture)
{
    /// <summary>筛选及取消不切换目录，明确选择容器后清除旧对象状态，同值可回根目录。</summary>
    [Fact]
    public Task BucketSelection_OnlyCommitNavigatesAndResetsObjectState() => fixture.RunAsync(async () =>
    {
        var service = CreateService();
        var source = new ObjectStorageWorkspaceDataSource();
        var editor = new FakeInstanceEditor();
        var factory = new InstanceWorkspaceFactory([new ObjectStoragePageProvider(service, new ObjectStorageTestDialogs(), editor)]);
        await using var shell = new ShellViewModel(source, editor, new FakeTerminalSessionFactory(),
            new ThemeService(Application.Current.Resources), pageFactory: factory);
        var instance = Assert.Single(shell.Sidebar.AllInstances);
        shell.Workspace.OpenInstance(instance);
        var model = Assert.IsType<ObjectStorageWorkspaceViewModel>(shell.Workspace.SelectedTab);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.NavigateAsync(service.Buckets[0], "releases/v0.9.0/", TestContext.Current.CancellationToken);
        await model.SearchAsync("config", TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(entry => !entry.IsDirectory), TestContext.Current.CancellationToken);
        model.SelectedEntry!.IsChecked = true;
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        SearchableSelect? select = null;
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            select = Assert.IsType<SearchableSelect>(Assert.Single(WindowTestVisuals.Descendants<ObjectStorageListView>(view)).FindName("BucketSelector"));
            var popup = Assert.IsType<Popup>(select.FindName("OptionsPopup"));
            popup.Child.Opacity = 0;
            int calls = service.ListCalls.Count;
            select.IsDropDownOpen = true;
            await WpfTestFixture.DrainDispatcherAsync();
            select.SearchText = "assets-prod";
            Assert.Equal("assets-dev", select.SelectedLabel);
            Assert.Single(select.Options);
            Press(Assert.IsType<TextBox>(select.FindName("FilterInput")), Key.Escape);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(select.IsDropDownOpen);
            Assert.Equal(calls, service.ListCalls.Count);
            Assert.Equal("releases/v0.9.0/", model.Prefix);
            Assert.Equal("config", model.SearchText);
            Assert.Equal(1, model.CheckedCount);
            Assert.NotNull(model.Details);
            Assert.Same(instance, Assert.Single(shell.Sidebar.AllInstances));
            Assert.True(instance.IsSelected);

            select.IsDropDownOpen = true;
            await WpfTestFixture.DrainDispatcherAsync();
            select.SearchText = "assets-prod";
            Press(Assert.IsType<TextBox>(select.FindName("FilterInput")), Key.Enter);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(calls + 1, service.ListCalls.Count);
            Assert.Equal(("assets-prod", "", "", (string?)null), service.ListCalls[^1]);
            Assert.Same(service.Buckets[1], model.SelectedBucket);
            Assert.Equal("", model.SearchText);
            Assert.Equal(0, model.CheckedCount);
            Assert.Null(model.SelectedEntry);
            Assert.Null(model.Details);
            Assert.Empty(model.DirectoryBreadcrumbs);
            Assert.Same(model, shell.Workspace.SelectedTab);
            Assert.True(instance.IsSelected);
            Assert.Equal(1, service.BucketReads);

            await model.NavigateAsync(service.Buckets[1], "releases/v0.9.0/", TestContext.Current.CancellationToken);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(new[] { "releases", "v0.9.0" }, model.DirectoryBreadcrumbs.Select(item => item.Name));
            Assert.True(model.DirectoryBreadcrumbs.Last().IsCurrent);
            Assert.False(model.DirectoryBreadcrumbs.First().IsCurrent);
            calls = service.ListCalls.Count;
            select.IsDropDownOpen = true;
            await WpfTestFixture.DrainDispatcherAsync();
            select.SearchText = "assets-prod";
            Press(Assert.IsType<TextBox>(select.FindName("FilterInput")), Key.Enter);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(calls + 1, service.ListCalls.Count);
            Assert.Equal("", model.Prefix);
            Assert.Equal("assets-prod", select.SelectedLabel);
            Assert.NotNull(BindingOperations.GetBindingExpression(select, SearchableSelect.SelectedItemProperty));
        }
        finally { if (select is not null) select.IsDropDownOpen = false; window.Close(); }
    });

    /// <summary>半选标题复选框经原生 Toggle 全选，再次 Toggle 清空，详情和绑定始终保留。</summary>
    [Fact]
    public Task PartiallyCheckedHeader_ToggleSelectsAllThenClears() => fixture.RunAsync(async () =>
    {
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(CreateService());
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var file = model.Entries.Single(entry => !entry.IsDirectory);
        await model.SelectEntryAsync(file, TestContext.Current.CancellationToken);
        file.IsChecked = true;
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var header = Assert.Single(WindowTestVisuals.Descendants<SelectionCheckBox>(view));
            Assert.Null(header.IsChecked);
            var peer = Assert.IsAssignableFrom<AutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(header));
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
            toggle.Toggle();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            Assert.All(model.Entries, entry => Assert.True(entry.IsChecked));
            Assert.Same(file, model.SelectedEntry);
            Assert.NotNull(BindingOperations.GetBindingExpression(header, CheckBox.IsCheckedProperty));
            toggle.Toggle();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            Assert.Equal(0, model.CheckedCount);
            Assert.Same(file, model.SelectedEntry);
            Assert.NotNull(BindingOperations.GetBindingExpression(header, CheckBox.IsCheckedProperty));
        }
        finally { window.Close(); }
    });

    /// <summary>目录更多按钮的 Enter 不被行导航抢占；列表行本身仍支持 Enter 进入目录。</summary>
    [Fact]
    public Task DirectoryMoreButton_EnterIsReservedForButtonInsteadOfNavigation() => fixture.RunAsync(async () =>
    {
        using var previews = new ContextMenuPreviewScope();
        var service = CreateService();
        var pending = new TaskCompletionSource<ObjectStoragePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ListObjectsHandler = (_, _, prefix, _, _, _) => prefix == "docs/" ? pending.Task
            : Task.FromResult(new ObjectStoragePage([new("docs/", true), new("config.json", false, 1024)]));
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.NavigationChanged += (_, _) => { if (model.Prefix == "docs/" && !model.IsLoading) navigated.TrySetResult(); };
        var view = new ObjectStorageView { DataContext = model };
        var window = CreateWindow(view);
        ContextMenu? menu = null;
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var list = Assert.Single(WindowTestVisuals.Descendants<ListBox>(view));
            var directory = model.Entries.Single(entry => entry.IsDirectory);
            list.SelectedItem = directory;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(list), item => AutomationProperties.GetName(item) == "docs/ 更多操作");
            int calls = service.ListCalls.Count;
            var key = Press(button, Key.Enter);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(key.Handled);
            Assert.Equal("", model.Prefix);
            Assert.Equal(calls, service.ListCalls.Count);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            menu = Assert.IsType<ContextMenu>(button.ContextMenu);
            Assert.True(menu.IsOpen);
            Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.Same(directory, item.CommandParameter));
            menu.IsOpen = false;
            var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(directory));
            key = Press(row, Key.Enter);
            // 网络尚未返回时就消费事件，避免目录导航继续传给父级快捷键和默认列表处理。
            Assert.True(key.Handled);
            Assert.Equal("docs/", model.Prefix);
            Assert.True(model.IsLoading);
            Assert.Equal(calls + 1, service.ListCalls.Count);
            pending.SetResult(new ObjectStoragePage([new("docs/file.txt", false, 32)]));
            await navigated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(model.IsLoading);
            Assert.Equal("docs/file.txt", Assert.Single(model.Entries).Key);
        }
        finally { pending.TrySetResult(new ObjectStoragePage([])); if (menu is not null) menu.IsOpen = false; window.Close(); }
    });

    /// <summary>通过真实路由传递键盘事件，不直接调用控件私有处理器。</summary>
    private static KeyEventArgs Press(UIElement element, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        element.RaiseEvent(args);
        return args;
    }
    /// <summary>本地替身只返回当前查询中的目录和文件。</summary>
    private static ObjectStorageTestService CreateService() => new()
    {
        Buckets = [new("assets-dev", "cn-hangzhou"), new("assets-prod", "cn-shanghai")],
        ListObjectsHandler = (_, _, prefix, _, _, _) => Task.FromResult(new ObjectStoragePage([new(prefix + "docs/", true), new(prefix + "config.json", false, 1024)]))
    };
    /// <summary>与设计组件相同宽度的透明真实窗口。</summary>
    private static HermodWindow CreateWindow(ObjectStorageView view) => new() { Content = view, Width = 1120, Height = 820 };
}
