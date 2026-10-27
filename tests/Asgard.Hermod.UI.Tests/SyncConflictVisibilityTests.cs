using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>同步冲突必须在未打开菜单的账户与实例名称处可见，并随解决或签出恢复。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SyncConflictVisibilityTests(WpfTestFixture fixture)
{
    /// <summary>真实列表和标题栏覆盖双语、英文回退、双主题与紧凑账户入口。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("fr-FR", UiTheme.Light)]
    public Task ConflictIsVisibleBesideNamesAndClearsWithoutChangingNames(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            var source = new ObjectStorageWorkspaceDataSource
            {
                Instance = new("conflict", "Production bastion", "Production bastion", "terminal", [],
                    TypeId: "ssh", ProviderId: "ssh")
                { ConnectionAddress = "example.test:22" },
            };
            var accounts = new AccountUiTestService { Username = "hermod-admin" };
            var sync = new AutomaticSyncUiTestService();
            await using var shell = new ShellViewModel(source, new FakeInstanceEditor(), new FakeTerminalSessionFactory(), themes, accounts: accounts, sync: sync);
            await accounts.SignInAsync();
            shell.Main.SelectedNavigation = shell.Main.Navigation.Single(item => item.Id == "ssh");
            var menu = new AccountMenu { DataContext = shell, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(10) };
            var content = new AppShellView { DataContext = shell };
            var main = Assert.IsType<MainWorkspaceView>(content.FindName("MainContent"));
            var root = new Grid { Background = (Brush)Application.Current.FindResource("SurfaceBrush") };
            root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
            root.Children.Add(menu); root.Children.Add(content); Grid.SetRow(content, 1);
            var host = new UserControl { Content = root, DataContext = shell };
            foreach (var state in new[] { ConfigurationSyncStatus.Conflict, ConfigurationSyncStatus.Pending, ConfigurationSyncStatus.Synced, ConfigurationSyncStatus.Conflict })
            {
                sync.SetStatus(state); await shell.Sidebar.RefreshAsync();
                await SshKubernetesTests.Layout(host, 1100, 620);
                bool conflict = state == ConfigurationSyncStatus.Conflict;
                var item = Assert.Single(shell.Sidebar.AllInstances);
                Assert.Equal(conflict, item.HasSyncConflict); Assert.Equal("Production bastion", item.Name);
                var name = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(main), text => text.Name == "InstanceNameText");
                var badge = Assert.Single(WindowTestVisuals.Descendants<Border>(main), border => border.Name == "InstanceConflictBadge");
                Assert.Equal(conflict ? Visibility.Visible : Visibility.Collapsed, badge.Visibility);
                Assert.Same(Application.Current.FindResource(conflict ? "DangerBrush" : "TextPrimaryBrush"), name.Foreground);
                var accountName = Assert.IsType<TextBlock>(menu.FindName("AccountNameText"));
                Assert.Same(Application.Current.FindResource(conflict ? "DangerBrush" : "TextBodyBrush"), accountName.Foreground);
                var marker = Assert.IsType<Border>(menu.FindName("AccountConflictMarker"));
                Assert.Equal(conflict ? Visibility.Visible : Visibility.Collapsed, marker.Visibility);
                Assert.False(Assert.IsType<Button>(menu.FindName("AccountButton")).ContextMenu.IsOpen);
                if (conflict)
                {
                    Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(badge), text => text.Text == Text.Get("SyncBadgeConflict"));
                    Assert.Equal(Text.Get("InstanceSyncConflict"), badge.ToolTip);
                    var nameRow = Assert.IsType<DockPanel>(badge.Parent);
                    Assert.True(badge.TranslatePoint(new(), nameRow).X + badge.ActualWidth <= nameRow.ActualWidth + 1);
                    SshKubernetesTests.Capture(host, $"sync-conflict-{language}-{theme}");
                }
            }
            menu.IsCompact = true; await SshKubernetesTests.Layout(host, 1100, 620);
            Assert.Equal(Visibility.Visible, Assert.IsType<Border>(menu.FindName("AccountConflictMarker")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(menu.FindName("AccountConflictLabel")).Visibility);
            await accounts.SignOutAsync(); await shell.Sidebar.RefreshAsync(); await SshKubernetesTests.Layout(host, 1100, 620);
            Assert.False(Assert.Single(shell.Sidebar.AllInstances).HasSyncConflict);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(menu.FindName("AccountConflictMarker")).Visibility);
        }
        finally { Text.SetApplicationCulture(null); themes.SetTheme(UiTheme.Light); }
    });
}
