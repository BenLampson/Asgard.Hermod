using System.Globalization;
using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>实际渲染图标与对象列表，验证本地化、换肤和窄视口不丢失操作。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class FileTypeIconViewTests(WpfTestFixture fixture)
{
    /// <summary>所有类别图标都有可绘制几何，现有控件的颜色随主题资源变化。</summary>
    [Fact]
    public Task EveryCategory_RendersAndUpdatesThemeColor() => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string key in new[] { "a.mp4", "a.mp3", "a.png", "a.svg", "a.pdf", "a.docx", "a.xlsx",
            "a.pptx", "a.md", "a.zip", "a.cs", "a.html", "a.json", "a.db", "a.ttf", "a.exe", "a.sh", "a.pem", "a.iso", "a.unknown" })
        {
            var type = FileTypeCatalog.Resolve(key);
            panel.Children.Add(new IconView { Kind = type.Icon, ForegroundResourceKey = type.BrushKey, Width = 24, Height = 24 });
        }
        var window = new HermodWindow { Content = panel, Width = 700, Height = 180 };
        try
        {
            WindowPreviewCapture.Show(window);
            Color? previousVideoColor = null;
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark, UiTheme.Light })
            {
                themes.SetTheme(theme);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                foreach (IconView icon in panel.Children)
                {
                    Assert.Same(Application.Current.Resources[icon.ForegroundResourceKey!], icon.Foreground);
                    var glyph = new IconView { Kind = icon.Kind, Foreground = icon.Foreground, Width = 24, Height = 24 };
                    glyph.Measure(new Size(24, 24));
                    glyph.Arrange(new Rect(0, 0, 24, 24));
                    glyph.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(glyph);
                    var pixels = new byte[24 * 24 * 4];
                    bitmap.CopyPixels(pixels, 24 * 4, 0);
                    Assert.True(Enumerable.Range(0, 24 * 24).Count(index => pixels[index * 4 + 3] > 0) > 20, icon.Kind);
                }
                var color = Assert.IsType<SolidColorBrush>(((IconView)panel.Children[0]).Foreground).Color;
                if (previousVideoColor is { } previous) Assert.NotEqual(previous, color);
                previousVideoColor = color;
            }
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>真实宽列表、窄列表和键盘选择保持类型色，英文和中文使用各自资源。</summary>
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, "视频 · MP4")]
    [InlineData("en", UiTheme.Dark, "Video · MP4")]
    public Task ObjectList_ShowsLocalizedTypesAndRetainsColorWhileSelected(string language, UiTheme theme, string label) => fixture.RunAsync(async () =>
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage(
                [new("1786011192035_4789_video_2085307433554677760_1786011190536.mp4", false, 6815744),
                new("storyboard_02.mov", false, 4299161), new("voiceover.mp3", false, 3879731), new("cover.png", false, 1992294),
                new("story-outline.docx", false, 251904), new("shot-list.xlsx", false, 131072), new("project-brief.pdf", false, 2306867),
                new("assets.tar.gz", false, 25794969), new("config.json", false, 2457)]))
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        var view = new ObjectStorageListView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1240, Height = 790 };
        try
        {
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var rows = Assert.Single(WindowTestVisuals.Descendants<ListBox>(view));
            var video = model.Entries.Single(entry => entry.Icon == "video" && entry.Key.EndsWith("mp4", StringComparison.Ordinal));
            Assert.Equal(label, video.TypeName);
            Assert.Equal(Visibility.Visible, view.FileTypeColumnVisibility);
            var icon = Assert.Single(WindowTestVisuals.Descendants<IconView>(rows), item => ReferenceEquals(item.DataContext, video) && item.Kind == "video");
            var color = icon.Foreground;
            rows.SelectedItem = video;
            rows.Focus();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(color, icon.Foreground);
            Assert.Equal("video", model.DetailIcon);
            await WindowPreviewCapture.VerifyAsync(window, $"file-types-{language}-{theme}");

            window.Width = 760;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(Visibility.Collapsed, view.FileTypeColumnVisibility);
            var name = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(rows), text => text.Text == video.Name);
            Assert.True(name.ActualWidth >= 100);
            Assert.Contains(WindowTestVisuals.Descendants<Button>(rows), button => button.IsVisible && button.ActualWidth >= 28);
            await WindowPreviewCapture.VerifyAsync(window, $"file-types-narrow-{language}-{theme}");
            window.Width = 1240;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(Visibility.Visible, view.FileTypeColumnVisibility);
            Assert.Equal(video, rows.SelectedItem);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previousCulture; }
    });

    /// <summary>用户读取元数据后，无后缀对象的行与详情同步更新，且不提前请求详情。</summary>
    [Fact]
    public Task ExtensionlessObject_UsesRequestedMetadataInRowAndDetails() => fixture.RunAsync(async () =>
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("stream", false)])),
            DetailsHandler = (_, _, key, _) => Task.FromResult(new ObjectStorageObjectDetails(key, 100, null, "Standard", "video/mp4", "private"))
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var row = Assert.Single(model.Entries);
        Assert.Equal("file", row.Icon);
        Assert.Empty(service.DetailsCalls);
        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        await model.SelectEntryAsync(row, TestContext.Current.CancellationToken);
        Assert.Equal("video", row.Icon);
        Assert.Equal(row.Icon, model.DetailIcon);
        Assert.Equal(row.IconBrushKey, model.DetailIconBrushKey);
        Assert.Contains(nameof(row.TypeName), changes);
        Assert.Single(service.DetailsCalls);
    });
}
