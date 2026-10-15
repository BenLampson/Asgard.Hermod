using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>离屏验证组合输入区，避免操作用户桌面，同时覆盖实际模板、绑定和滚动布局。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshAgentComposerTests(WpfTestFixture fixture)
{
    /// <summary>中英文与两种主题下只有外框，提示与正文对齐，窄屏长草稿仍能滚动编辑。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task ComposerKeepsOneFrameAndAlignedEditableContent(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var old = CultureInfo.CurrentUICulture;
        var oldDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        Text.Initialize(language, CultureInfo.InvariantCulture);
        themes.SetTheme(theme);
        using var model = new SshAssistantViewModel(null, () => null, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs());
        var view = new SshAssistantView { DataContext = model };
        try
        {
            await LayoutAsync(view, 420);
            var composer = Assert.IsType<Border>(view.FindName("Composer"));
            var prompt = Assert.IsType<TextBox>(view.FindName("PromptEditor"));
            var placeholder = Assert.IsType<TextBlock>(view.FindName("PromptPlaceholder"));
            Assert.Equal(Text.Get("SshAgentPrompt"), placeholder.Text);
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
            Assert.Equal(prompt.FontSize, placeholder.FontSize);
            Assert.Equal(new Thickness(0), prompt.Padding);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<Border>(prompt), border => border.BorderThickness != new Thickness(0));
            var content = Assert.IsType<ScrollViewer>(prompt.Template.FindName("PART_ContentHost", prompt));
            Assert.Equal(prompt.TranslatePoint(new Point(), composer), placeholder.TranslatePoint(new Point(), composer));
            var origin = prompt.TranslatePoint(new Point(), composer);
            Assert.True(origin.X >= 12 && origin.Y >= 12);
            Capture(view, $"ssh-composer-empty-{language}-{theme}");

            prompt.Text = "first line\nsecond line";
            prompt.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            await LayoutAsync(view, 320);
            Assert.Equal(prompt.Text, model.Prompt);
            Assert.Equal(Visibility.Collapsed, placeholder.Visibility);
            Assert.Equal(2, prompt.LineCount);
            model.Prompt = string.Join('\n', Enumerable.Repeat("long draft", 30));
            await LayoutAsync(view, 320);
            Assert.Equal(model.Prompt, prompt.Text);
            Assert.InRange(prompt.ActualHeight, 48, 100);
            Assert.True(content.ScrollableHeight > 0);
            var send = Assert.IsType<Button>(view.FindName("SendAction"));
            Assert.True(send.TranslatePoint(new Point(send.ActualWidth, send.ActualHeight), composer).X <= composer.ActualWidth);
            Capture(view, $"ssh-composer-draft-{language}-{theme}");

            model.Prompt = "";
            await LayoutAsync(view, 420);
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
        }
        finally
        {
            themes.SetTheme(UiTheme.Light);
            CultureInfo.CurrentUICulture = old;
            CultureInfo.DefaultThreadCurrentUICulture = oldDefault;
        }
    });

    /// <summary>不创建 HWND，使用与真实窗口相同的 WPF 模板和排版引擎。</summary>
    private static async Task LayoutAsync(FrameworkElement view, double width)
    {
        await WpfTestFixture.DrainDispatcherAsync();
        view.Measure(new Size(width, 600));
        view.Arrange(new Rect(0, 0, width, 600));
        view.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync();
    }

    /// <summary>按需导出实际离屏渲染供目视核对，图片不进入 Git。</summary>
    private static void Capture(FrameworkElement view, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
