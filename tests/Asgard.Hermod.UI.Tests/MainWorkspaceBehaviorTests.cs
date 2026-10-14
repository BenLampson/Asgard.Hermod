using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Asgard.Hermod.UI.Views.Shell;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过真实控件和数据服务验证导航、选择、筛选与跨重启访问事实。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWorkspaceBehaviorTests(WpfTestFixture fixture)
{
    /// <summary>单击选择不连接；显式打开复用同一独立窗口，地址搜索和类型筛选组合生效。</summary>
    [Fact]
    public Task SelectionFilteringAndExplicitOpen_UseRealWorkspacePath() => fixture.RunAsync(async () =>
    {
        var source = new Source(); var factory = new FakeTerminalSessionFactory();
        await using var shell = new ShellViewModel(source, new FakeInstanceEditor(), factory, new ThemeService(Application.Current.Resources));
        using var host = new WorkspaceWindowHost(shell.Workspace);
        var window = new Asgard.Hermod.MainWindow(shell); WindowPreviewCapture.Show(window);
        try
        {
            shell.Main.SelectedNavigation = shell.Main.Navigation[1];
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var rows = Assert.IsType<ListBox>(WindowTestVisuals.Named(window, "InstanceRows"));
            rows.SelectedIndex = 0; await WpfTestFixture.DrainDispatcherAsync();
            Assert.Empty(host.Windows);
            var selected = Assert.IsType<MainInstanceRow>(rows.SelectedItem);
            var rowControl = Assert.IsType<ListBoxItem>(rows.ItemContainerGenerator.ContainerFromIndex(0));
            WindowInteractionNativeMethods.PostDoubleClick(window, rowControl); await WpfTestFixture.DrainDispatcherAsync();
            var work = Assert.Single(host.Windows);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var more = WindowTestVisuals.Descendants<Button>(window).First(button => button.Name == "MoreButton");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WpfTestFixture.DrainDispatcherAsync();
            var menu = more.ContextMenu!;
            Point menuOrigin = menu.PointToScreen(new Point()), windowOrigin = window.PointToScreen(new Point());
            Assert.True(menuOrigin.X + menu.ActualWidth <= windowOrigin.X + window.ActualWidth + 1);
            WindowInteractionNativeMethods.PostPopupEscape(menu); await WpfTestFixture.DrainDispatcherAsync(); Assert.False(menu.IsOpen);
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WpfTestFixture.DrainDispatcherAsync();
            Assert.IsType<MenuItem>(menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Same(work, Assert.Single(host.Windows)); menu.IsOpen = false;
            Assert.Equal(0, factory.OpenAttempts);
            shell.Main.SelectedNavigation = shell.Main.Navigation[2];
            Assert.All(shell.Main.Rows, row => Assert.Equal("mysql", row.Instance.Summary.TypeId));
            shell.Main.Query = "10.0.0.2:3306"; Assert.Equal("two", Assert.Single(shell.Main.Rows).Instance.Id);
            shell.Main.Query = ""; shell.Sidebar.ApplyTagFilter(["Environment/Production"], TagFilterMatchMode.Any);
            Assert.Equal("one", Assert.Single(shell.Main.Rows).Instance.Id);
        }
        finally { await window.RequestExitAsync(sessionEnding: true); }
    });

    /// <summary>切换固定页签丢弃未确认标签草稿，保留已应用过滤；Ctrl+F 会切到列表并聚焦。</summary>
    [Fact]
    public Task Navigation_DiscardsPopupDraftAndCtrlFFocusesSearch() => fixture.RunAsync(async () =>
    {
        await using var shell = new ShellViewModel(new Source(), new FakeInstanceEditor(), new FakeTerminalSessionFactory(), new ThemeService(Application.Current.Resources));
        var window = new Asgard.Hermod.MainWindow(shell); WindowPreviewCapture.Show(window);
        try
        {
            shell.Main.SelectedNavigation = shell.Main.Navigation[1];
            shell.Sidebar.ApplyTagFilter(["Environment/Production"], TagFilterMatchMode.Any);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var button = Assert.IsType<Button>(WindowTestVisuals.Named(window, "TagFilterButton")); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            var view = Assert.IsType<MainWorkspaceView>(WindowTestVisuals.Descendants<MainWorkspaceView>(window).Single());
            var popup = Assert.IsType<Popup>(view.FindName("TagFilterPopup")); var picker = Assert.IsType<TagTreePicker>(view.FindName("TagPicker"));
            var draft = Assert.IsType<TagTreePickerViewModel>(picker.Model); draft.ClearCommand.Execute(null);
            shell.Main.SelectedNavigation = shell.Main.Navigation[0];
            Assert.False(popup.IsOpen); Assert.Equal(new[] { "Environment/Production" }, shell.Sidebar.SelectedTagPaths);
            view.FocusSearch(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(shell.Main.IsOverview); Assert.True(Assert.IsType<TextBox>(view.FindName("SearchInput")).IsKeyboardFocused);
        }
        finally { await window.RequestExitAsync(sessionEnding: true); }
    });

    /// <summary>连续访问七个实例后重启，完整最后访问事实和排序均保留，概览仍仅六行。</summary>
    [Fact]
    public Task SevenVisits_PersistBeyondOverviewAndRemoveDeletedHistory() => fixture.RunAsync(async () =>
    {
        using var storage = new LocalStorageTestEnvironment(); using var provider = storage.CreateServices();
        provider.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = provider.GetRequiredService<IRecentInstanceStore>();
        var source = new Source(); source.Items = Enumerable.Range(0, 7).Select(index => Item("item-" + index, "mysql", "10.0.0." + index + ":3306")).ToArray();
        await using (var first = new ShellViewModel(source, new FakeInstanceEditor(), new FakeTerminalSessionFactory(), new ThemeService(Application.Current.Resources), recentInstances: store))
        {
            foreach (var item in first.Sidebar.AllInstances) first.Workspace.OpenInstance(item);
            Assert.Equal(7, first.Workspace.Home.VisitHistory.Count); Assert.Equal(6, first.Main.Rows.Count);
        }
        using var restartedProvider = storage.CreateServices(); restartedProvider.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = restartedProvider.GetRequiredService<IRecentInstanceStore>(); Assert.Equal(7, restored.Load().Count);
        await using var restarted = new ShellViewModel(source, new FakeInstanceEditor(), new FakeTerminalSessionFactory(), new ThemeService(Application.Current.Resources), recentInstances: restored);
        restarted.Main.SelectedNavigation = restarted.Main.Navigation[1];
        Assert.Equal(7, restarted.Main.Rows.Count); Assert.All(restarted.Main.Rows, row => Assert.NotNull(row.OpenedAt));
        Assert.Equal("item-6", restarted.Main.Rows[0].Instance.Id); Assert.Equal("item-0", restarted.Main.Rows[^1].Instance.Id);
        source.Items = source.Items.Where(item => item.Id != "item-0").ToArray(); await restarted.Sidebar.RefreshAsync();
        Assert.DoesNotContain(restored.Load(), item => item.InstanceId == "item-0");
    });

    private static InstanceSummary Item(string id, string type, string address) => new(id, id, id, "database", [id == "one" ? "Environment/Production" : "Environment/Development"], TypeId: type, ProviderId: type + ".direct")
    { ConnectionAddress = address };
    private sealed class Source : IWorkspaceDataSource
    {
        internal InstanceSummary[] Items = [Item("one", "mysql", "10.0.0.1:3306"), Item("two", "mysql", "10.0.0.2:3306"), Item("three", "redis", "10.0.0.3:6379")];
        public IReadOnlyList<InstanceSummary> GetInstances() => Items;
        public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
    }
}
