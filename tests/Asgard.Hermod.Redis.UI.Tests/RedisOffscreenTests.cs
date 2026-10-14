using System.Globalization;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>无 HWND 回归：真实事件目标、共享元数据命令、主题及离屏对齐。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisOffscreenTests(WpfTestFixture fixture)
{
    [Fact]
    public Task LeafClick_KeepsAncestorExpansionSelectionAndLoadedNodes() => fixture.RunOffscreenAsync(async () =>
    {
        var session = new RedisTestSession { Scan = (_, _, _) => Task.FromResult(new RedisScanPage("0", [RedisKeyId.FromText("cache:key")])) };
        var configs = new RedisTestConfigurations();
        await using var page = new RedisWorkspaceViewModel(new(configs.CurrentInstance), configs, session, configs, new RedisTestDialogs());
        await page.InitializeAsync();
        var group = Assert.Single(page.Tree!.Root.Children); group.IsExpanded = true;
        await page.Tree.LoadAsync(group);
        var leaf = Assert.Single(group.Children);
        var view = new RedisWorkspaceView { DataContext = page }; Layout(view);
        var tree = (WorkspaceTreeView)view.FindName("KeyTree");
        var groupItem = Assert.IsAssignableFrom<TreeViewItem>(tree.ItemContainerGenerator.ContainerFromItem(group));
        var leafItem = Assert.IsAssignableFrom<TreeViewItem>(groupItem.ItemContainerGenerator.ContainerFromItem(leaf));
        leafItem.IsSelected = true;
        int scans = session.Calls.Count;
        Click(view, leafItem);
        Assert.True(group.IsExpanded); Assert.Same(leaf, tree.SelectedItem);
        Assert.Same(leaf, Assert.Single(group.Children)); Assert.Equal(scans, session.Calls.Count);
        Assert.Same(leaf.Key, page.ActiveTab!.Key);
        Assert.Null(tree.FocusVisualStyle); Assert.Null(leafItem.FocusVisualStyle);
        Click(view, groupItem); Assert.False(group.IsExpanded);
        Click(view, groupItem); Assert.True(group.IsExpanded);
        Assert.Equal(scans, session.Calls.Count);
    });

    [Fact]
    public Task Rename_UsesCatalogSaveAndUpdatesPageWithoutReopeningSession() => fixture.RunOffscreenAsync(async () =>
    {
        var configs = new RedisTestConfigurations(); var session = new RedisTestSession();
        var factory = new InstanceWorkspaceFactory([new RedisPageProvider(configs, session, configs, new RedisTestDialogs())]);
        await using var shell = new ShellViewModel(configs, configs, new RedisTestTerminalFactory(), new ThemeService(Application.Current.Resources), pageFactory: factory);
        var instance = Assert.Single(shell.Sidebar.AllInstances); shell.Workspace.OpenInstance(instance);
        var page = Assert.IsType<RedisWorkspaceViewModel>(shell.Workspace.SelectedTab); await page.InitializeAsync();
        await page.OpenAsync(RedisKeyId.FromText("cache:key")); var tab = page.ActiveTab!;
        await tab.EditCommand.ExecuteAsync(); tab.EditorText = "kept draft";
        int reads = session.Reads, scans = session.Calls.Count;
        var view = new RedisWorkspaceView { DataContext = page };
        InstanceMetadataScope.SetRenameCommand(view, shell.Workspace.RenameInstanceCommand); Layout(view);
        var name = Descendants<InstanceNameButton>(view).Single();
        var button = (Button)name.FindName("RenameButton");
        Assert.Same(shell.Sidebar.RenameInstanceCommand, button.Command);
        Assert.True(button.Command.CanExecute(button.CommandParameter));
        await shell.Sidebar.RenameInstanceCommand.ExecuteAsync(button.CommandParameter);
        Assert.Equal(1, configs.Renames); Assert.Equal("Renamed cache", instance.Name); Assert.Equal(instance.Name, page.Header);
        Assert.Same(instance, name.Instance); Assert.Same(tab, page.ActiveTab); Assert.True(tab.IsDirty);
        Assert.Equal("kept draft", tab.EditorText); Assert.Equal(reads, session.Reads); Assert.Equal(scans, session.Calls.Count);
        Assert.True(page.IsConnected);
    });

    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Chrome_AlignsAndUsesGlobalThemeWithoutWindows(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
            var configs = new RedisTestConfigurations(); var session = new RedisTestSession();
            await using var shell = new ShellViewModel(configs, configs, new RedisTestTerminalFactory(), themes);
            await using var page = new RedisWorkspaceViewModel(new(configs.CurrentInstance), configs, session, configs, new RedisTestDialogs());
            await page.InitializeAsync(); await page.OpenAsync(RedisKeyId.FromText("cache:key"));
            var view = new RedisWorkspaceView { DataContext = page }; Layout(view);
            var title = new TitleBarView { DataContext = shell }; Layout(title, 1320, 42);
            var toggle = (Button)title.FindName("ThemeButton");
            Assert.Same(shell.ToggleThemeCommand, toggle.Command);
            Assert.Equal(Text.Get(theme == UiTheme.Light ? "SwitchToDarkMode" : "SwitchToLightMode"), AutomationProperties.GetName(toggle));
            Assert.Equal(toggle.ToolTip, AutomationProperties.GetName(toggle));
            var database = (ComboBox)view.FindName("DatabaseSelector");
            var databaseField = (StackPanel)view.FindName("DatabaseField");
            Assert.Equal(Orientation.Horizontal, databaseField.Orientation);
            Assert.Equal(32, database.ActualHeight);
            var layout = (WorkspacePageLayout)view.FindName("WorkspaceLayout");
            var status = Assert.IsType<DockPanel>(layout.StatusContent);
            var statusBar = (Border)layout.Template.FindName("PART_StatusBar", layout);
            var presenter = Assert.IsType<ContentPresenter>(statusBar.Child);
            Assert.Equal(VerticalAlignment.Center, presenter.VerticalAlignment);
            Assert.All(status.Children.Cast<TextBlock>(), text => Assert.Equal(VerticalAlignment.Center, text.VerticalAlignment));
            var tabs = (Asgard.Hermod.UI.Controls.WorkspaceTabStrip)view.FindName("KeyTabs");
            Assert.Equal(view.FindResource("SurfaceBrush"), tabs.Background);
            Assert.NotNull(tabs.ItemContainerStyle);
            Assert.All(Descendants<SectionSelector>(view), selector => Assert.Equal(new Thickness(0), selector.BorderThickness));
            Capture(view, $"redis-{language}-{theme}"); Capture(title, $"title-{language}-{theme}");
            toggle.Command.Execute(null); Layout(view); Layout(title, 1320, 42);
            Assert.Equal(theme != UiTheme.Dark, shell.IsDarkTheme);
            Assert.Equal(view.FindResource("SurfaceBrush"), tabs.Background);
            Assert.Equal(Text.Get(theme == UiTheme.Light ? "SwitchToLightMode" : "SwitchToDarkMode"), AutomationProperties.GetName(toggle));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private static void Click(RedisWorkspaceView view, TreeViewItem item)
    {
        var source = Descendants<ContentPresenter>(item).First();
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = source };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, 1);
        typeof(RedisWorkspaceView).GetMethod("OnTreeClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [view, args]);
    }
    private static void Layout(FrameworkElement view, double width = 1200, double height = 700)
    {
        // 离屏根不参与 Application.Windows 的资源广播，显式注册共享字典的视图所有者。
        if (!view.Resources.MergedDictionaries.Contains(Application.Current.Resources))
            view.Resources.MergedDictionaries.Add(Application.Current.Resources);
        view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout();
        Assert.Null(PresentationSource.FromVisual(view)); Assert.Null(Window.GetWindow(view));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var matchChild in Descendants<T>(child)) yield return matchChild;
        }
    }
    private static void Capture(FrameworkElement view, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
}
