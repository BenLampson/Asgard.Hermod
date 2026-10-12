using System.Collections.ObjectModel;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实搜索单选弹层回归，筛选候选与已经确认的业务值始终隔离。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SearchableSelectTests(WpfTestFixture fixture)
{
    /// <summary>筛选及零结果不修改选中值或数据源，Escape 放弃候选并恢复完整列表。</summary>
    [Theory]
    [InlineData("ASSETS", 1)]
    [InlineData("不存在的 Bucket", 0)]
    public Task FilteringAndEscape_PreserveSelectionAndOriginalSource(string filter, int count) =>
        fixture.RunAsync(async () =>
        {
            var (select, source, window) = Create();
            var original = source.ToArray();
            int commits = 0;
            select.SelectionCommitted += (_, _) => commits++;
            try
            {
                var (popup, input, _) = await OpenAsync(select, window);
                input.Text = filter;
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.Equal(filter, select.SearchText);
                Assert.Equal(count, select.Options.Count);
                Assert.Equal(count > 0, select.HasOptions);
                Assert.Same(original[0], select.SelectedItem);
                Assert.Equal(original[0].Name, select.SelectedLabel);
                Assert.Equal(original, source.ToArray());
                Assert.Equal(0, commits);
                if (count == 0)
                    Assert.Equal(Visibility.Visible, Assert.IsType<StackPanel>(select.FindName("EmptyOptions")).Visibility);

                var key = RaiseKey(input, Key.Escape);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.True(key.Handled);
                Assert.False(popup.IsOpen);
                Assert.False(select.IsDropDownOpen);
                Assert.Equal("", select.SearchText);
                Assert.Same(original[0], select.SelectedItem);
                Assert.Equal(original, source.ToArray());
                Assert.Equal(source.Count, select.Options.Count);
                Assert.Same(original[0], Assert.Single(select.Options, option => option.IsCommitted).Value);
                Assert.Equal(0, commits);
            }
            finally { select.IsDropDownOpen = false; window.Close(); }
        });

    /// <summary>搜索 Enter 明确确认一次，同值确认也发事件，并保留双向业务绑定。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SearchEnter_CommitsNewOrCurrentValueExactlyOnce(bool sameValue) => fixture.RunAsync(async () =>
    {
        var (select, source, window) = Create();
        var bindingSource = new Border { Tag = source[0] };
        BindingOperations.SetBinding(select, SearchableSelect.SelectedItemProperty,
            new Binding(nameof(Border.Tag)) { Source = bindingSource, Mode = BindingMode.TwoWay });
        var expected = sameValue ? source[0] : source[2];
        var committed = new List<object?>();
        select.SelectionCommitted += (_, _) => committed.Add(select.SelectedItem);
        try
        {
            var (popup, input, _) = await OpenAsync(select, window);
            input.Text = expected.Name;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Same(source[0], select.SelectedItem);
            Assert.Empty(committed);
            var key = RaiseKey(input, Key.Enter);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(key.Handled);
            Assert.Same(expected, select.SelectedItem);
            Assert.Same(expected, bindingSource.Tag);
            Assert.Same(expected, Assert.Single(committed));
            Assert.Equal(expected.Name, select.SelectedLabel);
            Assert.False(popup.IsOpen);
            Assert.True(BindingOperations.IsDataBound(select, SearchableSelect.SelectedItemProperty));
        }
        finally { select.IsDropDownOpen = false; window.Close(); }
    });

    /// <summary>列表候选变化只更新预选，只有经过真实路由 Enter 才提交。</summary>
    [Fact]
    public Task OptionsCandidate_CommitsOnlyAfterEnter() => fixture.RunAsync(async () =>
    {
        var (select, source, window) = Create();
        int commits = 0;
        select.SelectionCommitted += (_, _) => commits++;
        try
        {
            var (popup, _, list) = await OpenAsync(select, window);
            list.SelectedItem = select.Options[1];
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Same(source[0], select.SelectedItem);
            Assert.Equal(0, commits);
            var key = RaiseKey(list, Key.Enter);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(key.Handled);
            Assert.Same(source[1], select.SelectedItem);
            Assert.Equal(1, commits);
            Assert.False(popup.IsOpen);
        }
        finally { select.IsDropDownOpen = false; window.Close(); }
    });

    /// <summary>可观察集合增加和删除实时更新筛选候选，清空搜索恢复当前完整源。</summary>
    [Fact]
    public Task ObservableSourceChanges_RefreshFilteredOptionsWithoutChangingSelection() => fixture.RunAsync(async () =>
    {
        var (select, source, window) = Create();
        int commits = 0;
        select.SelectionCommitted += (_, _) => commits++;
        try
        {
            var (_, input, _) = await OpenAsync(select, window);
            input.Text = "assets";
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Single(select.Options);
            var added = new ObjectStorageBucket("assets-archive", "cn-hangzhou");
            source.Add(added);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(2, select.Options.Count);
            Assert.Contains(select.Options, option => ReferenceEquals(option.Value, added));
            source.RemoveAt(2);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Same(added, Assert.Single(select.Options).Value);
            input.Text = "";
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(source.Cast<object>(), select.Options.Select(option => option.Value));
            Assert.Same(source[0], select.SelectedItem);
            Assert.Equal(0, commits);
        }
        finally { select.IsDropDownOpen = false; window.Close(); }
    });

    /// <summary>空匹配通过实际清空按钮恢复候选，清空动作不会确认或关闭弹层。</summary>
    [Fact]
    public Task EmptyResults_ClearButtonRestoresOptionsWithoutCommitting() => fixture.RunAsync(async () =>
    {
        var (select, source, window) = Create();
        int commits = 0;
        select.SelectionCommitted += (_, _) => commits++;
        try
        {
            var (popup, input, _) = await OpenAsync(select, window);
            input.Text = "missing";
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(select.HasOptions);
            RaiseKey(input, Key.Enter);
            Assert.Same(source[0], select.SelectedItem);
            Assert.Equal(0, commits);
            var clear = Assert.Single(WindowTestVisuals.Descendants<Button>(popup.Child),
                button => Equals(button.Content, "清空筛选"));
            Assert.True(clear.IsVisible);
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal("", input.Text);
            Assert.Equal(source.Count, select.Options.Count);
            Assert.True(select.HasOptions);
            Assert.True(popup.IsOpen);
            Assert.Same(source[0], select.SelectedItem);
            Assert.Equal(0, commits);
        }
        finally { select.IsDropDownOpen = false; window.Close(); }
    });

    /// <summary>大量候选只滚动选项区，搜索与计数位置固定，浅深主题分别渲染核对。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task ManyOptions_ScrollIndependentlyOfSearchAndCount(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var (select, source, window) = Create();
        foreach (int number in Enumerable.Range(1, 40))
            source.Add(new($"bucket-{number:00}", "cn-hangzhou"));
        try
        {
            var (_, input, list) = await OpenAsync(select, window);
            var chrome = Assert.IsType<Border>(select.FindName("OptionsChrome"));
            var count = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(chrome), text => text.Text == select.OptionCountText);
            Point searchBefore = input.TranslatePoint(new Point(), chrome);
            Point countBefore = count.TranslatePoint(new Point(), chrome);
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(list));
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.Equal(28, input.ActualHeight);
            Assert.InRange(list.ActualHeight, 1, 224);
            scroll.ScrollToEnd();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(searchBefore, input.TranslatePoint(new Point(), chrome));
            Assert.Equal(countBefore, count.TranslatePoint(new Point(), chrome));
            Assert.Same(source[0], select.SelectedItem);
            Capture(chrome, $"searchable-select-{theme}");
        }
        finally { select.IsDropDownOpen = false; themes.SetTheme(UiTheme.Light); window.Close(); }
    });

    /// <summary>已确认项位于虚拟列表后部时，搜索向下键滚入该候选并交给列表确认。</summary>
    [Theory]
    [InlineData(29)]
    [InlineData(62)]
    public Task FilterDown_RevealsAndFocusesCommittedCandidateBeforeEnter(int selectedIndex) => fixture.RunAsync(async () =>
    {
        var (select, source, window) = Create();
        foreach (int number in Enumerable.Range(1, 60))
            source.Add(new($"bucket-{number:00}", "cn-hangzhou"));
        var original = source[selectedIndex];
        select.SelectedItem = original;
        int commits = 0;
        select.SelectionCommitted += (_, _) => commits++;
        try
        {
            var (popup, input, list) = await OpenAsync(select, window);
            var candidate = Assert.IsType<SelectOption>(list.SelectedItem);
            Assert.Same(original, candidate.Value);
            Assert.Equal(original.Name, select.SelectedLabel);
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(list));
            // 用户滚动浏览其它位置后，键盘仍需回到当前候选；不依赖虚拟化缓存是否保留容器。
            scroll.ScrollToTop();
            await WindowTestVisuals.UpdateLayoutAsync(window);

            string focusedBefore = DescribeFocus(Keyboard.FocusedElement);
            string logicalBefore = DescribeFocus(FocusManager.GetFocusedElement(FocusManager.GetFocusScope(input)));

            // 非活动 HWND 无法接收原生键盘焦点，清空作用域以允许产品 Focus() 使用逻辑焦点回退。
            // 官方回退条件见 https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs。
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(input), null);

            var down = RaiseKey(input, Key.Down);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(down.Handled);
            var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(candidate));
            var focusScope = FocusManager.GetFocusScope(container);
            var logicalFocus = FocusManager.GetFocusedElement(focusScope);
            string focusDiagnostic =
                $"SelectedIndex={selectedIndex}; KeyboardBefore={focusedBefore}; LogicalBefore={logicalBefore}; "
                + $"KeyboardAfter={DescribeFocus(Keyboard.FocusedElement)}; Input={DescribeFocus(input)}; "
                + $"Container={DescribeFocus(container)}; List={DescribeFocus(list)}; "
                + $"FocusScope={DescribeFocus(focusScope)}; LogicalAfter={DescribeFocus(logicalFocus)}; "
                + $"WindowActive={window.IsActive}; PopupOpen={popup.IsOpen}; "
                + $"ContainerSource={PresentationSource.FromVisual(container)?.GetType().Name}";
            // 透明且不激活的测试 HWND 没有原生键盘焦点；验证产品 Focus() 实际设置的逻辑焦点。
            Assert.True(ReferenceEquals(container, logicalFocus), focusDiagnostic);
            Assert.True(container.IsFocused, focusDiagnostic);
            Assert.True(container.IsVisible);
            var viewport = Assert.Single(WindowTestVisuals.Descendants<ScrollContentPresenter>(scroll));
            var bounds = new Rect(container.TranslatePoint(new Point(), viewport),
                new Size(container.ActualWidth, container.ActualHeight));
            Assert.True(bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1,
                $"候选应滚入选项视口，Actual={bounds}; Viewport={viewport.ActualHeight}。");
            Assert.Same(original, select.SelectedItem);
            Assert.Equal(0, commits);
            Assert.True(popup.IsOpen);

            var enter = RaiseKey(container, Key.Enter);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(enter.Handled);
            Assert.Same(original, select.SelectedItem);
            Assert.Equal(1, commits);
            Assert.False(popup.IsOpen);
        }
        finally { select.IsDropDownOpen = false; window.Close(); }
    });

    /// <summary>使用实际 Bucket 模型和 Name 路径，避免以 ToString 掩盖显示成员错误。</summary>
    private static (SearchableSelect Select, ObservableCollection<ObjectStorageBucket> Source, HermodWindow Window) Create()
    {
        ObservableCollection<ObjectStorageBucket> source =
        [new("production-east", "cn-hangzhou"), new("staging", "cn-shanghai"), new("assets-dev", "cn-hangzhou")];
        var select = new SearchableSelect
        {
            ItemsSource = source,
            DisplayMemberPath = nameof(ObjectStorageBucket.Name),
            SelectedItem = source[0],
            Width = 170,
            VerticalAlignment = VerticalAlignment.Top,
        };
        return (select, source, new HermodWindow { Content = select, Width = 360, Height = 180 });
    }

    /// <summary>点击产品按钮打开真实 Popup，像素透明但保留绑定、事件和独立 HWND。</summary>
    private static async Task<(Popup Popup, TextBox Input, ListBox List)> OpenAsync(SearchableSelect select, HermodWindow window)
    {
        var popup = Assert.IsType<Popup>(select.FindName("OptionsPopup"));
        popup.PopupAnimation = PopupAnimation.None;
        Assert.IsType<Grid>(popup.Child).Opacity = 0;
        WindowPreviewCapture.Show(window);
        await WindowTestVisuals.UpdateLayoutAsync(window);
        Assert.IsType<Button>(select.FindName("TriggerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WindowTestVisuals.UpdateLayoutAsync(window);
        Assert.True(popup.IsOpen);
        Assert.True(select.IsDropDownOpen);
        return (popup, Assert.IsType<TextBox>(select.FindName("FilterInput")), Assert.IsType<ListBox>(select.FindName("OptionsList")));
    }

    /// <summary>通过真实 HWND 的路由键盘事件走产品处理器，不直接调用私有方法。</summary>
    private static KeyEventArgs RaiseKey(UIElement target, Key key)
    {
        var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(target));
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>失败时同时报告键盘与逻辑焦点，区分候选错误与隐藏非活动 HWND 的限制。</summary>
    private static string DescribeFocus(object? element) => element is FrameworkElement view
        ? $"{view.GetType().Name}({view.Name}; Visible={view.IsVisible}; Loaded={view.IsLoaded}; "
          + $"Focusable={view.Focusable}; Enabled={view.IsEnabled}; Focused={view.IsFocused}; "
          + $"KeyboardWithin={view.IsKeyboardFocusWithin}; KeyboardFocused={view.IsKeyboardFocused}; Data={view.DataContext})"
        : element?.ToString() ?? "null";

    /// <summary>按需渲染弹层边框，忽略外层测试透明度，不显示到桌面。</summary>
    private static void Capture(Border chrome, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(chrome.ActualWidth),
            (int)Math.Ceiling(chrome.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(chrome);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
