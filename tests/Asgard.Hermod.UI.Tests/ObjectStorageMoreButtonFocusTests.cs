using System.Windows.Automation;
using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>回归 OSS 行末按钮完整边界和鼠标菜单关闭后的焦点提示，保持键盘导航可用。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageMoreButtonFocusTests(WpfTestFixture fixture)
{
    /// <summary>更多按钮不被图标列裁切，键盘提示完整出现；鼠标恢复焦点不留下按钮或整行蓝线。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task MoreButton_KeyboardCueIsUnclippedAndMouseMenuLeavesNoBlueLines(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var menus = new ContextMenuPreviewScope();
        using var focus = new KeyboardFocusPreviewScope();
        var service = new ObjectStorageTestService
        {
            Buckets = [new("assets-dev", "cn-hangzhou")],
            ListObjectsHandler = (_, _, prefix, _, _, _) => Task.FromResult(new ObjectStoragePage([new(prefix + "config.json", false, 1024)]))
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new ObjectStorageView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1120, Height = 820 };
        ContextMenu? menu = null;
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var list = Assert.Single(WindowTestVisuals.Descendants<ListBox>(view));
            var entry = Assert.Single(model.Entries, item => !item.IsDirectory);
            var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(entry));
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(row), item =>
                AutomationProperties.GetName(item) == entry.Name + " 更多操作");
            Assert.True(button.Focusable && KeyboardNavigation.GetIsTabStop(button));
            Assert.Equal(28, button.ActualWidth);
            Assert.Equal(28, button.ActualHeight);
            var clip = VisualTreeHelper.GetClip(button);
            Assert.True(clip is null || clip.Bounds.Contains(new Rect(button.RenderSize)),
                $"完整按钮不应被 16px 图标列裁切，Clip={clip?.Bounds}; Size={button.RenderSize}。");
            var bounds = new Rect(button.TranslatePoint(new Point(), row), button.RenderSize);
            Assert.True(new Rect(row.RenderSize).Contains(bounds));
            foreach (double edge in new[] { 1d, 27d })
            {
                var hit = Assert.IsAssignableFrom<DependencyObject>(row.InputHitTest(button.TranslatePoint(new Point(edge, 14), row)));
                Assert.True(ReferenceEquals(button, hit) || button.IsAncestorOf(hit), $"按钮边缘 {edge}px 必须仍属于更多按钮的命中区域。");
            }

            focus.ShowKeyboard(button);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var adorner = Assert.Single(KeyboardFocusPreviewScope.Adorners(button));
            var ring = Assert.Single(WindowTestVisuals.Descendants<Border>(adorner));
            Assert.Equal(28, ring.ActualWidth);
            Assert.Equal(28, ring.ActualHeight);
            Assert.Equal(new Thickness(2), ring.BorderThickness);
            Assert.False(adorner.IsHitTestVisible);
            Assert.Equal(BrushColor("AccentTextBrush"), Assert.IsType<SolidColorBrush>(ring.BorderBrush).Color);

            // 真实框架失焦处理器也必须清除焦点层，不依赖鼠标或菜单继续触发绘制。
            button.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, button, list)
            { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.Empty(KeyboardFocusPreviewScope.Adorners(button));
            focus.ShowKeyboard(button);
            Assert.Single(KeyboardFocusPreviewScope.Adorners(button));

            focus.ShowMouseFocus(button);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            menu = Assert.IsType<ContextMenu>(button.ContextMenu);
            Assert.True(menu.IsOpen);
            Assert.Same(entry, model.SelectedEntry);
            menu.IsOpen = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            focus.ShowMouseFocus(button);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Empty(KeyboardFocusPreviewScope.Adorners(button));
            Assert.Empty(KeyboardFocusPreviewScope.Adorners(row));
            Assert.Null(button.Template.FindName("FocusRing", button));
            var rowChrome = Assert.IsType<Border>(row.Template.FindName("Row", row));
            Assert.Equal(BrushColor("LineSoftBrush"), Assert.IsType<SolidColorBrush>(rowChrome.BorderBrush).Color);
            Assert.Equal(BrushColor("AccentWeakBrush"), Assert.IsType<SolidColorBrush>(rowChrome.Background).Color);
            Assert.Same(entry, model.SelectedEntry);
            Assert.NotNull(model.Details);

            focus.ShowKeyboard(row);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Single(KeyboardFocusPreviewScope.Adorners(row));
            focus.ShowMouseFocus(row);
            Assert.Empty(KeyboardFocusPreviewScope.Adorners(row));
        }
        finally { if (menu is not null) menu.IsOpen = false; themes.SetTheme(UiTheme.Light); window.Close(); }
    });

    /// <summary>沿真实主题资源获取动态颜色，两个主题分别验证。</summary>
    private static Color BrushColor(string key) => Assert.IsType<SolidColorBrush>(Application.Current.FindResource(key)).Color;
}
