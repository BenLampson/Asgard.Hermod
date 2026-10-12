using System.Windows.Input;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证共享控件的实际排版、设计主题状态与只读可复制语义。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SharedControlStateTests(WpfTestFixture fixture)
{
    /// <summary>图标与文字组合内容也继承字体和前景，主操作色与禁用状态遵循最新语义色。</summary>
    [Theory]
    [InlineData(UiTheme.Light, "#2F6BE0")]
    [InlineData(UiTheme.Dark, "#3468CD")]
    public Task PrimaryButton_UsesActionColorAndComposedTextInheritance(UiTheme theme, string color) =>
        fixture.RunAsync(async () =>
        {
            var themes = new ThemeService(Application.Current.Resources);
            themes.SetTheme(theme);
            var label = new TextBlock { Text = "上传" };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new IconView { Kind = "upload", Margin = new Thickness(0, 0, 8, 0) });
            content.Children.Add(label);
            // 透明 HWND 仍可能位于真实鼠标下；此用例验证默认态，避免桌面指针影响颜色断言。
            var button = new Button
            {
                Content = content,
                Style = (Style)Application.Current.FindResource("PrimaryButton"),
                IsHitTestVisible = false,
            };
            var window = CreateWindow(button);
            try
            {
                window.Show();
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Equal(ColorConverter.ConvertFromString(color), Assert.IsType<SolidColorBrush>(button.Background).Color);
                Assert.Equal(32, button.Height);
                Assert.Equal(new Thickness(14, 0, 14, 0), button.Padding);
                Assert.Equal(button.FontFamily, label.FontFamily);
                Assert.Equal(button.FontSize, label.FontSize);
                Assert.Equal(FontWeights.SemiBold, label.FontWeight);
                Assert.Equal(BrushColor("InverseTextBrush"), Assert.IsType<SolidColorBrush>(label.Foreground).Color);

                button.IsEnabled = false;
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Equal(BrushColor("SidebarBrush"), Assert.IsType<SolidColorBrush>(button.Background).Color);
                Assert.Equal(BrushColor("TextMutedBrush"), Assert.IsType<SolidColorBrush>(label.Foreground).Color);
                Assert.False(button.Focus());
            }
            finally { themes.SetTheme(UiTheme.Light); window.Close(); }
        });

    /// <summary>2px 内描边出现时不会改变按钮文字或输入内容的宽度和位置。</summary>
    [Theory]
    [InlineData("Button")]
    [InlineData("TextBox")]
    [InlineData("SecretInput")]
    public Task FocusDecoration_DoesNotMoveContent(string kind) => fixture.RunAsync(async () =>
    {
        Control input = kind switch
        {
            "Button" => new Button { Content = "保存" },
            "TextBox" => new TextBox { Text = "instance-001" },
            _ => new SecretInput { Value = "example" },
        };
        var host = new Grid();
        host.Children.Add(input);
        var window = CreateWindow(host);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var content = WindowTestVisuals.Descendants<FrameworkElement>(input).First(element => kind switch
            {
                "Button" => element is ContentPresenter,
                "TextBox" => element.Name == "PART_ContentHost",
                _ => element is PasswordBox,
            });
            Point before = content.TranslatePoint(new Point(), input);
            Size sizeBefore = new(content.ActualWidth, content.ActualHeight);
            Control? cue = null;
            if (kind == "Button")
            {
                // 按钮的焦点层由 WPF 叠加；此处只验证叠层模板不会改变正文排版。
                cue = new Control { Style = input.FocusVisualStyle, IsHitTestVisible = false };
                host.Children.Add(cue);
                cue.ApplyTemplate();
            }
            var focus = Assert.IsType<Border>((cue ?? input).Template.FindName("FocusRing", cue ?? input));
            Assert.Equal(new Thickness(2), focus.BorderThickness);
            Assert.False(cue?.IsHitTestVisible ?? focus.IsHitTestVisible);
            focus.Visibility = Visibility.Visible;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(before, content.TranslatePoint(new Point(), input));
            Assert.Equal(sizeBefore, new Size(content.ActualWidth, content.ActualHeight));
        }
        finally { window.Close(); }
    });

    /// <summary>只读输入仍能选取文本并使用原生复制命令，禁用输入无法获得焦点。</summary>
    [Fact]
    public Task ReadOnlyInput_AllowsCopyWhileDisabledInputCannotFocus() => fixture.RunAsync(async () =>
    {
        var input = new TextBox { Text = "instance-001", IsReadOnly = true };
        var window = CreateWindow(input);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(36, input.MinHeight);
            Assert.Equal(BrushColor("AppBackgroundBrush"), Assert.IsType<SolidColorBrush>(input.Background).Color);
            input.SelectAll();
            Assert.Equal(input.Text, input.SelectedText);
            Assert.True(ApplicationCommands.Copy.CanExecute(null, input));
            input.IsEnabled = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(BrushColor("SidebarBrush"), Assert.IsType<SolidColorBrush>(input.Background).Color);
            Assert.False(input.Focus());
        }
        finally { window.Close(); }
    });

    /// <summary>标题、正文和页脚都能承载原生键盘焦点层，鼠标恢复焦点时清理而不移动正文。</summary>
    [Theory]
    [InlineData("Caption")]
    [InlineData("Body")]
    [InlineData("Footer")]
    public Task WindowSlots_ShareKeyboardOnlyButtonDecoration(string slot) => fixture.RunAsync(async () =>
    {
        using var focus = new KeyboardFocusPreviewScope();
        var button = new Button { Content = "保存" };
        var window = CreateWindow(new Grid());
        if (slot == "Caption") window.CaptionContent = button;
        else if (slot == "Footer") window.FooterContent = button;
        else window.Content = button;
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var content = Assert.Single(WindowTestVisuals.Descendants<ContentPresenter>(button));
            Point before = content.TranslatePoint(new Point(), button);
            Size sizeBefore = content.RenderSize;
            focus.ShowKeyboard(button);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var adorner = Assert.Single(KeyboardFocusPreviewScope.Adorners(button));
            Assert.False(adorner.IsHitTestVisible);
            var ring = Assert.Single(WindowTestVisuals.Descendants<Border>(adorner));
            Assert.Equal(new Thickness(2), ring.BorderThickness);
            Assert.Equal(button.RenderSize, ring.RenderSize);
            Assert.Equal(before, content.TranslatePoint(new Point(), button));
            Assert.Equal(sizeBefore, content.RenderSize);
            focus.ShowMouseFocus(button);
            Assert.Empty(KeyboardFocusPreviewScope.Adorners(button));
        }
        finally { window.Close(); }
    });

    /// <summary>通过真实主题字典读取动态资源的最终颜色。</summary>
    private static Color BrushColor(string key) => Assert.IsType<SolidColorBrush>(Application.Current.FindResource(key)).Color;

    /// <summary>透明窗口保留 WPF 模板和布局流程，不显示在桌面或夺取焦点。</summary>
    private static HermodWindow CreateWindow(FrameworkElement content) => new()
    {
        Content = content,
        Width = 360,
        Height = 180,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
    };
}
