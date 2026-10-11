using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>自定义共享菜单模板仍保留 WPF 子菜单、勾选状态和命令执行语义。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MenuInteractionTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>使用实际标题栏菜单和主题命令，不创建简化的测试菜单。</summary>
    public MenuInteractionTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>主题子菜单能够实际展开并执行切换，已勾选标记随现有绑定更新。</summary>
    [Fact]
    public Task ThemeSubmenu_OpensExecutesCommandAndUpdatesCheckmarks() => _fixture.RunAsync(async () =>
    {
        using var previews = new ContextMenuPreviewScope();
        await using var environment = new ShellTestEnvironment();
        var view = new TitleBarView { DataContext = environment.Shell };
        var window = new HermodWindow { Content = view, Width = 1000, Height = 200 };
        ContextMenu? menu = null;
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var button = Assert.IsType<Button>(view.FindName("SettingsButton"));
            menu = Assert.IsType<ContextMenu>(button.ContextMenu);
            menu.PlacementTarget = button;
            menu.IsOpen = true;
            await WpfTestFixture.DrainDispatcherAsync();
            var header = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Header is "Theme");
            var popup = Assert.IsType<Popup>(header.Template.FindName("PART_Popup", header));
            Assert.IsAssignableFrom<UIElement>(popup.Child).Opacity = 0;
            var peer = new MenuItemAutomationPeer(header);
            Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse)).Expand();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(header.IsSubmenuOpen);
            Assert.True(popup.IsOpen);
            var light = Assert.Single(header.Items.OfType<MenuItem>(), item => item.Header is "Light");
            var dark = Assert.Single(header.Items.OfType<MenuItem>(), item => item.Header is "Dark");
            Assert.True(light.IsChecked);
            Assert.False(dark.IsChecked);
            Assert.Equal(Visibility.Visible, Assert.IsType<IconView>(light.Template.FindName("CheckMark", light)).Visibility);

            await ContextMenuPreviewScope.InvokeAsync(dark);
            Assert.Equal(UiTheme.Dark, environment.Themes.Theme);
            Assert.True(dark.IsChecked);
            Assert.False(light.IsChecked);
            Assert.False(menu.IsOpen);
            Assert.False(popup.IsOpen);
        }
        finally
        {
            if (menu is not null) menu.IsOpen = false;
            window.Close();
            environment.Themes.SetTheme(UiTheme.Light);
        }
    });
}
