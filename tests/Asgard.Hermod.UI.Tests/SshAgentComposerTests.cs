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
        var user = new SshAssistantMessage("Check server resources", true, 0);
        var reply = new SshAssistantMessage("Review the output\n```bash\nuname -a\n```", false, 0);
        var tool = new SshAssistantMessage("uname -a\nLinux host", false, 0, false, "SshAgentDiagnosticStep", true);
        model.Messages.Add(user);
        model.Messages.Add(tool);
        model.Messages.Add(reply);
        var view = new SshAssistantView { DataContext = model };
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(view, false);
        try
        {
            await LayoutAsync(view, 420);
            var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
            var composer = Assert.IsType<Border>(panel.FindName("Composer"));
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            var placeholder = Assert.IsType<TextBlock>(panel.FindName("PromptPlaceholder"));
            Assert.Equal(Text.Get("SshAgentPrompt"), placeholder.Text);
            Assert.Equal(Visibility.Visible, placeholder.Visibility);
            Assert.Equal(prompt.FontSize, placeholder.FontSize);
            Assert.Equal(new Thickness(0), prompt.Padding);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<Border>(prompt), border => border.BorderThickness != new Thickness(0));
            var bubbles = WindowTestVisuals.Descendants<Border>(view).Where(border => border.Name == "MessageBubble").ToArray();
            Assert.Equal(3, bubbles.Length);
            var userBubble = Assert.Single(bubbles, border => ReferenceEquals(border.DataContext, user));
            var toolBubble = Assert.Single(bubbles, border => ReferenceEquals(border.DataContext, tool));
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("AccentWeakBrush")).Color, Assert.IsType<SolidColorBrush>(userBubble.Background).Color);
            Assert.NotEqual(Assert.IsType<SolidColorBrush>(userBubble.Background).Color, Assert.IsType<SolidColorBrush>(toolBubble.Background).Color);
            foreach (var body in WindowTestVisuals.Descendants<TextBox>(view).Where(editor => editor.IsReadOnly))
            {
                Assert.True(body.Focusable);
                Assert.DoesNotContain(WindowTestVisuals.Descendants<Border>(body), border => border.BorderThickness != new Thickness(0));
                Assert.Null(body.Template.FindName("FocusRing", body));
                Assert.Equal(new Thickness(0), body.Padding);
                Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(body.Background).Color);
            }
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
            var send = Assert.IsType<Button>(panel.FindName("SendAction"));
            Assert.True(send.TranslatePoint(new Point(send.ActualWidth, send.ActualHeight), composer).X <= composer.ActualWidth);
            Capture(view, $"ssh-composer-draft-{language}-{theme}");

            model.Prompt = "";
            await LayoutAsync(view, 420);
            await LayoutAsync(view, 420);
            Assert.Empty(prompt.Text);
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
