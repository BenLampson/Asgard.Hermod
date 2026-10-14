using System.Globalization;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>公共导航切换只改变选中外观，所有页签、图标和文字的布局保持稳定。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainNavigationLayoutTests(WpfTestFixture fixture)
{
    /// <summary>真实模板在中英文、明暗主题及实际布局 DPI 下，任意两个页签间切换都不改变位置。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, 1d)]
    [InlineData("en", UiTheme.Light, 1.25d)]
    [InlineData("en", UiTheme.Light, 1.5d)]
    [InlineData("en", UiTheme.Light, 2d)]
    [InlineData("en", UiTheme.Dark, 1d)]
    [InlineData("en", UiTheme.Dark, 1.25d)]
    [InlineData("en", UiTheme.Dark, 1.5d)]
    [InlineData("en", UiTheme.Dark, 2d)]
    [InlineData("zh-Hans", UiTheme.Light, 1d)]
    [InlineData("zh-Hans", UiTheme.Light, 1.25d)]
    [InlineData("zh-Hans", UiTheme.Light, 1.5d)]
    [InlineData("zh-Hans", UiTheme.Light, 2d)]
    [InlineData("zh-Hans", UiTheme.Dark, 1d)]
    [InlineData("zh-Hans", UiTheme.Dark, 1.25d)]
    [InlineData("zh-Hans", UiTheme.Dark, 1.5d)]
    [InlineData("zh-Hans", UiTheme.Dark, 2d)]
    public Task SwitchingTabsPreservesLayoutAtEveryDpi(string language, UiTheme theme, double scale) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            await using var environment = new ShellTestEnvironment();
            environment.Themes.SetTheme(theme);
            var tabs = new MainNavigationTabs { ItemsSource = environment.Shell.Main.Navigation, SelectedIndex = 0 };
            var root = new Border { Child = tabs, UseLayoutRounding = true, SnapsToDevicePixels = true };
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            void Layout()
            {
                root.Measure(new Size(1000, 42));
                root.Arrange(new Rect(0, 0, 1000, 42));
                root.UpdateLayout();
            }
            Layout();
            Assert.Equal(scale, VisualTreeHelper.GetDpi(tabs).DpiScaleX);
            var expected = CaptureLayout(tabs, root);
            for (int previous = 0; previous < tabs.Items.Count; previous++)
            {
                for (int index = 0; index < tabs.Items.Count; index++)
                {
                    tabs.SelectedIndex = previous;
                    Layout();
                    tabs.SelectedIndex = index;
                    Layout();
                    Assert.Equal(expected, CaptureLayout(tabs, root));
                    AssertSelectedAppearance(tabs, index);
                }
            }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    /// <summary>实际主窗体在宽窄布局下通过辅助技术逐项选择，导航绑定、加粗和全部坐标都保持正确。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task MainWindowSelectionPreservesEveryTabPosition(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            await using var environment = new ShellTestEnvironment();
            environment.Themes.SetTheme(theme);
            var window = new Asgard.Hermod.MainWindow(environment.Shell) { Width = 1360, Height = 860 };
            try
            {
                WindowPreviewCapture.Show(window);
                var tabs = WindowTestVisuals.Descendants<MainNavigationTabs>(window).Single();
                foreach (double width in new[] { 1360d, 1000d })
                {
                    window.Width = width;
                    tabs.SelectedIndex = 0;
                    await WindowTestVisuals.UpdateLayoutAsync(window);
                    var frame = WindowTestVisuals.Frame(window);
                    var expected = CaptureLayout(tabs, frame);
                    foreach (int index in Enumerable.Range(0, tabs.Items.Count))
                    {
                        var peer = new ListBoxItemAutomationPeer(tabs.Items[index], new ListBoxAutomationPeer(tabs));
                        Assert.IsAssignableFrom<ISelectionItemProvider>(peer.GetPattern(PatternInterface.SelectionItem)).Select();
                        await WindowTestVisuals.UpdateLayoutAsync(window);
                        Assert.Same(environment.Shell.Main.Navigation[index], environment.Shell.Main.SelectedNavigation);
                        Assert.Equal(expected, CaptureLayout(tabs, frame));
                        AssertSelectedAppearance(tabs, index);
                    }
                }
            }
            finally { await window.RequestExitAsync(sessionEnding: true); }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    /// <summary>从真实显示树读取尺寸和原点，以精确坐标比较捕获不足一像素的移动。</summary>
    private static (Rect Container, Rect Icon, Rect Label)[] CaptureLayout(MainNavigationTabs tabs, FrameworkElement root)
        => Enumerable.Range(0, tabs.Items.Count).Select(index =>
        {
            var item = Assert.IsType<ListBoxItem>(tabs.ItemContainerGenerator.ContainerFromIndex(index));
            var icon = Assert.Single(WindowTestVisuals.Descendants<IconView>(item));
            var label = VisibleLabel(item);
            Rect Bounds(FrameworkElement element) => new(element.TranslatePoint(new Point(), root), element.RenderSize);
            return (Bounds(item), Bounds(icon), Bounds(label));
        }).ToArray();

    /// <summary>每个页签只展示一份标题；选中加粗、未选中普通字重，标题来自同一份本地化模型。</summary>
    private static void AssertSelectedAppearance(MainNavigationTabs tabs, int selectedIndex)
    {
        for (int index = 0; index < tabs.Items.Count; index++)
        {
            var item = Assert.IsType<ListBoxItem>(tabs.ItemContainerGenerator.ContainerFromIndex(index));
            var label = VisibleLabel(item);
            Assert.Equal(index == selectedIndex ? FontWeights.SemiBold : FontWeights.Normal, label.FontWeight);
            Assert.Equal(Assert.IsType<MainNavigationItem>(tabs.Items[index]).Title, label.Text);
        }
    }

    /// <summary>排除仅参与测量的隐藏标题，检查实际展示给用户的唯一文本。</summary>
    private static TextBlock VisibleLabel(ListBoxItem item)
        => Assert.Single(WindowTestVisuals.Descendants<TextBlock>(item), label => label.Visibility == Visibility.Visible);
}
