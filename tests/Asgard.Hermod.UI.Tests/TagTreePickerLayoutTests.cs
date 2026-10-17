using System.Windows.Controls.Primitives;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实有限空间布局回归，防止芯片和系统滚动条尺寸挤掉可操作内容。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TagTreePickerLayoutTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>在共享 STA 上创建真实窗口并读取完成后的布局。</summary>
    public TagTreePickerLayoutTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>九个独立芯片不能裁切应用按钮，320 DIP 下仍需保留一行树的操作空间。</summary>
    [Fact]
    public Task Height320_MultipleChipsKeepApplyVisibleAndTreeOperable() => _fixture.RunAsync(async () =>
    {
        var model = CreateModel();
        var picker = new TagTreePicker { Model = model, Height = 320 };
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(9, model.SelectedPaths.Count);
            Assert.Equal(31, model.Rows.Count);
            Assert.Equal(320d, picker.ActualHeight, 3);

            var apply = Assert.Single(WindowTestVisuals.Descendants<Button>(picker),
                button => ReferenceEquals(button.Command, model.ApplyCommand));
            Assert.True(apply.IsVisible);
            Assert.True(apply.IsEnabled);
            Rect pickerBounds = WindowTestVisuals.ScreenBounds(picker);
            Rect applyBounds = WindowTestVisuals.ScreenBounds(apply);
            AssertContained(pickerBounds, applyBounds, "多个标签芯片把应用按钮挤出了选择器。");

            var tree = TreeScrollViewer(picker, model);
            Assert.True(tree.ViewportHeight >= 28.5,
                $"有限高度下树列表应至少容纳一行标签；实际 ViewportHeight={tree.ViewportHeight} DIP。");
            Rect treeBounds = WindowTestVisuals.ScreenBounds(tree);
            AssertContained(pickerBounds, treeBounds, "树列表的操作空间超出选择器边界。");
            Assert.True(treeBounds.Bottom <= applyBounds.Top + 1,
                $"树列表与应用按钮重叠；Tree={treeBounds}; Apply={applyBounds}。");
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    });

    /// <summary>需要滚动时读取真实模板滚动条，不能只检查被系统模板覆盖的 Style.Width。</summary>
    [Fact]
    public Task Height480_VisibleTreeScrollbarUsesEightDipWidth() => _fixture.RunAsync(async () =>
    {
        var model = CreateModel();
        var picker = new TagTreePicker { Model = model, Height = 480 };
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(480d, picker.ActualHeight, 3);
            var tree = TreeScrollViewer(picker, model);
            Assert.True(tree.ScrollableHeight > 0, "测试标签必须多到需要竖向滚动。");
            var scrollbar = Assert.Single(WindowTestVisuals.Descendants<ScrollBar>(tree),
                bar => bar.Orientation == Orientation.Vertical && bar.IsVisible);

            double tolerance = 0.5 / VisualTreeHelper.GetDpi(scrollbar).DpiScaleX;
            Assert.InRange(scrollbar.ActualWidth, 8 - tolerance, 8 + tolerance);
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    });

    /// <summary>选择部分叶子确保产生九个独立芯片，不能被全选父路径压缩成一个。</summary>
    private static TagTreePickerViewModel CreateModel()
    {
        var labels = Enumerable.Range(1, 30).Select(number => new TagFilterDefinition(
            $"Independent label {number:00}", $"Services/Independent-label-{number:00}")).ToArray();
        return new TagTreePickerViewModel([new("Services", "Services", labels)],
            labels.Take(9).Select(label => label.Path).ToArray(), TagFilterMatchMode.Any);
    }

    /// <summary>通过真实 Rows 绑定定位树的 ScrollViewer，避免混淆芯片区域的滚动容器。</summary>
    private static ScrollViewer TreeScrollViewer(TagTreePicker picker, TagTreePickerViewModel model)
    {
        var items = Assert.Single(WindowTestVisuals.Descendants<ItemsControl>(picker),
            control => ReferenceEquals(control.ItemsSource, model.Rows));
        DependencyObject? parent = VisualTreeHelper.GetParent(items);
        while (parent is not null && parent is not ScrollViewer)
            parent = VisualTreeHelper.GetParent(parent);
        return Assert.IsType<ScrollViewer>(parent);
    }

    /// <summary>以物理像素比较全部边界，允许一像素的 DPI 布局舍入。</summary>
    private static void AssertContained(Rect outer, Rect inner, string reason) => Assert.True(
        inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1
        && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1,
        $"{reason} Picker={outer}; Element={inner}");

    /// <summary>窗口保留真实 HWND，主体空间大于显式设定的选择器高度。</summary>
    private static HermodWindow CreateWindow(TagTreePicker picker) => new()
    {
        Title = "Tag picker layout regression",
        Content = picker,
        Width = 420,
        Height = picker.Height + 80,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
    };
}
