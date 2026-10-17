using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实页签模板离屏验证横向滚轮，不发送桌面输入。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceTabScrollTests(WpfTestFixture fixture)
{
    /// <summary>双向滚动和边界钳制不改变选中页签，普通滚轮及组合键仍保留原行为。</summary>
    [Fact]
    public Task ShiftWheel_ScrollsViewportAndPreservesSelection() => fixture.RunOffscreenAsync(async () =>
    {
        var strip = new WorkspaceTabStrip { ItemsSource = Enumerable.Range(0, 12).Select(index => $"table_{index}").ToArray(), SelectedIndex = 0 };
        var root = new Border { Child = strip };
        root.Resources.MergedDictionaries.Add(Application.Current.Resources);
        Layout();
        var viewer = Assert.IsType<ScrollViewer>(strip.Template.FindName("PART_ScrollViewer", strip));
        Assert.True(viewer.ScrollableWidth > 48);
        object selected = strip.SelectedItem;

        foreach (var modifiers in new[] { ModifierKeys.None, ModifierKeys.Control, ModifierKeys.Shift | ModifierKeys.Control })
        {
            var args = Wheel(-120);
            strip.HandleMouseWheel(args, modifiers);
            Assert.False(args.Handled);
        }
        var handled = Wheel(-120); handled.Handled = true;
        strip.HandleMouseWheel(handled, ModifierKeys.Shift);
        await Update(); Assert.Equal(0, viewer.HorizontalOffset);

        await Scroll(-120); Assert.Equal(48, viewer.HorizontalOffset);
        await Scroll(-60); Assert.Equal(72, viewer.HorizontalOffset);
        await Scroll(120); Assert.Equal(24, viewer.HorizontalOffset);
        await Scroll(int.MinValue); Assert.Equal(viewer.ScrollableWidth, viewer.HorizontalOffset);
        await Scroll(-120); Assert.Equal(viewer.ScrollableWidth, viewer.HorizontalOffset);
        await Scroll(int.MaxValue); Assert.Equal(0, viewer.HorizontalOffset);
        await Scroll(120); Assert.Equal(0, viewer.HorizontalOffset);

        strip.ItemsSource = new[] { selected };
        await Update(); Assert.Equal(0, viewer.ScrollableWidth);
        var noOverflow = Wheel(-120); strip.HandleMouseWheel(noOverflow, ModifierKeys.Shift);
        Assert.False(noOverflow.Handled);

        void Layout() { root.Measure(new(360, 80)); root.Arrange(new(0, 0, 360, 80)); root.UpdateLayout(); }
        async Task Update() { await WpfTestFixture.DrainDispatcherAsync(); Layout(); }
        async Task Scroll(int delta)
        {
            var args = Wheel(delta); strip.HandleMouseWheel(args, ModifierKeys.Shift);
            Assert.True(args.Handled); await Update();
            Assert.Same(selected, strip.SelectedItem); Assert.Equal(0, viewer.VerticalOffset);
        }
    });

    private static MouseWheelEventArgs Wheel(int delta) => new(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
}
