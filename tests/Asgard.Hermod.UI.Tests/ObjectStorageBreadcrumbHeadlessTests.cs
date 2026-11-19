using System.Globalization;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在独立的 WPF 控件树中验证 Bucket 根目录、目录段和选择器，不创建窗口或弹层。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageBreadcrumbHeadlessTests(WpfTestFixture fixture)
{
    /// <summary>长名称在常用宽度下完整展示，父目录和 Bucket 均可通过各自的按钮直接返回。</summary>
    [Theory]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("en", UiTheme.Light)]
    public Task BucketAndAncestorButtonsNavigateToTheirOwnLevel(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        var previousTheme = themes.Theme;
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            themes.SetTheme(theme);
            var service = new ObjectStorageTestService
            {
                Buckets = [new("md-custom-service-assets", "example")],
                ListObjectsHandler = (_, _, prefix, _, _, _) => Task.FromResult<ObjectStoragePage>(prefix switch
                {
                    "" => new([new("bridge/", true), new("test/", true)]),
                    "bridge/" => new([new("bridge/uploads/", true)]),
                    _ => new([new("bridge/uploads/images/", true), new("bridge/uploads/manifest.json", false, 2048)])
                })
            };
            await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
            var token = TestContext.Current.CancellationToken;
            await model.InitializeAsync(token);
            var list = new ObjectStorageListView { DataContext = model };
            var surface = CreateSurface(list);
            await Layout(surface, 916, 440);
            var bucket = Assert.Single(model.Buckets);
            var root = (Button)list.FindName("BucketRootButton");
            var selector = (SearchableSelect)list.FindName("BucketSelector");
            var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(root));
            Assert.Equal(bucket.Name, label.Text);
            Assert.Equal(Text.Format("OpenBucketRoot", bucket.Name), AutomationProperties.GetName(root));
            Assert.Equal(Text.Get("SwitchBucket"), AutomationProperties.GetName((Button)selector.FindName("TriggerButton")));
            Assert.True(model.BucketBreadcrumb!.IsCurrent);
            AssertFullNameOnOneLine(label);
            Capture(surface, $"bucket-root-{language}");

            await model.OpenEntryAsync(model.Entries.First(), token);
            await model.OpenEntryAsync(Assert.Single(model.Entries), token);
            await Layout(surface, 916, 440);
            var parent = Assert.Single(WindowTestVisuals.Descendants<Button>(list), button => AutomationProperties.GetName(button) == "bridge");
            Assert.False(model.BucketBreadcrumb.IsCurrent);
            Assert.Equal("bridge/uploads/", model.Prefix);
            Assert.Equal(root.TranslatePoint(new(), list).Y, parent.TranslatePoint(new(), list).Y, 1);
            AssertFullNameOnOneLine(label);
            Capture(surface, $"bucket-folder-{language}");

            await Invoke(parent);
            Assert.Equal("bridge/", model.Prefix);
            await model.OpenEntryAsync(Assert.Single(model.Entries), token);
            await model.SearchAsync("manifest", token);
            var file = model.Entries.Single(entry => !entry.IsDirectory);
            await model.SelectEntryAsync(file, token);
            file.IsChecked = true;
            int reads = service.ListCalls.Count;
            await Invoke(root);
            Assert.Equal(reads + 1, service.ListCalls.Count);
            Assert.Equal((bucket.Name, "", "", (string?)null), service.ListCalls[^1]);
            Assert.Same(bucket, model.SelectedBucket);
            Assert.True(model.BucketBreadcrumb.IsCurrent);
            Assert.Empty(model.DirectoryBreadcrumbs);
            Assert.Null(model.SelectedEntry);
            Assert.Equal(0, model.CheckedCount);
            Assert.False(selector.IsDropDownOpen);
        }
        finally
        {
            Text.SetApplicationCulture(null);
            themes.SetTheme(previousTheme);
        }
    });

    /// <summary>窄列表给长桶名换行，保留独立的根目录和切换按钮；选择另一个 Bucket 进入它的根目录。</summary>
    [Fact]
    public Task NarrowListWrapsLongNamesAndKeepsBucketSwitchingIndependent() => fixture.RunOffscreenAsync(async () =>
    {
        var first = new ObjectStorageBucket("md-custom-service-transfers-and-preview-archive-for-production", "example");
        var second = new ObjectStorageBucket("md-custom-service-transfers-and-preview-archive-for-staging", "example");
        var service = new ObjectStorageTestService { Buckets = [first, second] };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        var token = TestContext.Current.CancellationToken;
        await model.InitializeAsync(token);
        await model.NavigateAsync(first, "bridge/uploads/previews/", token);
        var list = new ObjectStorageListView { DataContext = model };
        var surface = CreateSurface(list);
        await Layout(surface, 380, 600);
        var root = (Button)list.FindName("BucketRootButton");
        var selector = (SearchableSelect)list.FindName("BucketSelector");
        var refresh = Assert.Single(WindowTestVisuals.Descendants<Button>(list), button => AutomationProperties.GetName(button) == Text.Get("RefreshObjectList"));
        var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(root));
        Assert.Equal(first.Name, label.Text);
        Assert.True(root.ActualHeight > 32);
        Assert.True(root.TranslatePoint(new(root.ActualWidth, 0), list).X <= selector.TranslatePoint(new(), list).X);
        Assert.True(selector.TranslatePoint(new(selector.ActualWidth, 0), list).X <= refresh.TranslatePoint(new(), list).X);
        Assert.True(refresh.TranslatePoint(new(refresh.ActualWidth, 0), list).X < list.ActualWidth);
        Assert.True(root.Focusable);
        Assert.True(((Button)selector.FindName("TriggerButton")).Focusable);

        // 只排版弹层内容并向选项发送 WPF 路由事件，弹层始终关闭。
        int reads = service.ListCalls.Count;
        selector.SearchText = "staging";
        var popup = (Popup)selector.FindName("OptionsPopup");
        await Layout((FrameworkElement)popup.Child, 280, 160);
        var options = (ListBox)selector.FindName("OptionsList");
        var item = Assert.IsType<ListBoxItem>(options.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal(second.Name, Assert.Single(WindowTestVisuals.Descendants<TextBlock>(item)).Text);
        Assert.True(item.ActualHeight > 32);
        Assert.Equal(reads, service.ListCalls.Count);
        Assert.Same(first, model.SelectedBucket);
        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.False(popup.IsOpen);
        Assert.Same(second, model.SelectedBucket);
        Assert.Equal((second.Name, "", "", (string?)null), service.ListCalls[^1]);
        Assert.Equal(reads + 1, service.ListCalls.Count);
        Assert.Equal(second.Name, label.Text);
    });

    private static Border CreateSurface(ObjectStorageListView list)
    {
        var surface = new Border { Child = list };
        surface.Resources.MergedDictionaries.Add(Application.Current.Resources);
        surface.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        surface.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        return surface;
    }

    private static async Task Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new(width, height));
        element.Arrange(new(0, 0, width, height));
        element.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync();
        element.UpdateLayout();
        Assert.Null(PresentationSource.FromVisual(element));
    }

    private static void AssertFullNameOnOneLine(TextBlock label)
    {
        var natural = new TextBlock { Text = label.Text, FontSize = label.FontSize, FontFamily = label.FontFamily, FontWeight = label.FontWeight };
        natural.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(label.ActualWidth >= natural.DesiredSize.Width - 1);
        Assert.InRange(label.ActualHeight, natural.DesiredSize.Height - 1, natural.DesiredSize.Height + 1);
    }

    private static async Task Invoke(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        await WpfTestFixture.DrainDispatcherAsync();
    }

    private static void Capture(FrameworkElement surface, string name)
    {
        if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
