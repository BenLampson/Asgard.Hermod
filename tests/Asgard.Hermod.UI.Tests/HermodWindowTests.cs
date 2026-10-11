using System.Windows.Interop;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证公共窗口底座的真实 HWND、模板、主题与最大化行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class HermodWindowTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>在共享 STA 应用中运行窗口用例，不另建 Application。</summary>
    public HermodWindowTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>新增窗口必须自动采用统一底座，防止以后出现绕过公共外观的窗口。</summary>
    [Fact]
    public void ProductionWindows_AllInheritSharedHermodWindow()
    {
        var windowTypes = new[] { typeof(HermodWindow).Assembly, typeof(Asgard.Hermod.MainWindow).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && typeof(Window).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(windowTypes);
        Assert.All(windowTypes, type => Assert.True(typeof(HermodWindow).IsAssignableFrom(type),
            $"窗口 {type.FullName} 没有继承 HermodWindow，会绕过统一的标题栏与边框。"));
    }

    /// <summary>子窗复现 36 DIP 标题及 60 DIP 动作区，仅提供最小化和关闭。</summary>
    [Fact]
    public Task Dialog_DefaultTemplateKeepsCaptionAndFooterWithoutSystemCaption() => _fixture.RunAsync(async () =>
    {
        var content = new Grid();
        var footer = new Grid { Height = 60 };
        var dialog = new HermodDialog
        {
            Title = "Template dialog",
            Content = content,
            FooterContent = footer,
            Opacity = 0,
            ShowActivated = false,
        };
        try
        {
            Assert.Equal(ResizeMode.CanMinimize, dialog.ResizeMode);
            Assert.False(dialog.ShowInTaskbar);
            Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
            Assert.Null(dialog.CaptionContent);
            Assert.Equal(36d, dialog.CaptionHeight);
            Assert.Equal(620d, dialog.Width);
            Assert.Equal(460d, dialog.Height);

            dialog.Show();
            await WindowTestVisuals.UpdateLayoutAsync(dialog);

            Assert.Equal(WindowStyle.None, dialog.WindowStyle);
            Assert.False(WindowInteractionNativeMethods.HasSystemCaption(dialog));
            Assert.False(WindowInteractionNativeMethods.HasResizeFrame(dialog));
            var caption = WindowTestVisuals.Caption(dialog);
            Assert.Equal(36d, caption.ActualHeight, 3);
            var controls = Assert.Single(WindowTestVisuals.Descendants<WindowControls>(caption));
            var buttons = WindowTestVisuals.Descendants<Button>(controls).Where(item => item.IsVisible).ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.Single(buttons, button => button.Command == SystemCommands.MinimizeWindowCommand);
            var close = Assert.Single(buttons, button => button.Command == SystemCommands.CloseWindowCommand);

            Rect frameBounds = WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(dialog));
            Rect captionBounds = WindowTestVisuals.ScreenBounds(caption);
            Rect contentBounds = WindowTestVisuals.ScreenBounds(content);
            Rect footerBounds = WindowTestVisuals.ScreenBounds(footer);
            Assert.InRange(captionBounds.Top, frameBounds.Top - 1, frameBounds.Top + 1);
            Assert.InRange(contentBounds.Top, captionBounds.Bottom - 1, captionBounds.Bottom + 1);
            Assert.InRange(contentBounds.Bottom, footerBounds.Top - 1, footerBounds.Top + 1);
            Assert.InRange(footerBounds.Bottom, frameBounds.Bottom - 1, frameBounds.Bottom + 1);
            Assert.Equal(60d, footer.ActualHeight, 3);
            Assert.Equal(60d, WindowTestVisuals.Footer(dialog).ActualHeight, 3);

            WindowTestVisuals.Invoke(close);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
        }
    });

    /// <summary>已经显示的拥有者和对话框必须一起换肤，不重建模板或残留旧颜色。</summary>
    [Fact]
    public Task ApplicationThemeChange_UpdatesOwnerAndExistingDialog() => _fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(UiTheme.Light);
        var owner = CreateWindow(new Grid());
        var dialog = new HermodDialog
        {
            Title = "Themed dialog",
            Content = new Grid(),
            Width = 440,
            Height = 260,
            Opacity = 0,
            ShowActivated = false,
        };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            dialog.Show();
            await WindowTestVisuals.UpdateLayoutAsync(dialog);
            var ownerFrame = WindowTestVisuals.Frame(owner);
            var dialogFrame = WindowTestVisuals.Frame(dialog);
            var title = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(WindowTestVisuals.Caption(dialog)),
                text => text.Text == dialog.Title);
            Color lightFrame = BrushColor(dialogFrame.Background);
            Color lightTitle = BrushColor(title.Foreground);
            Color lightOwner = BrushColor(ownerFrame.Background);
            Assert.Equal(ResourceColor("SurfaceBrush"), lightFrame);

            themes.SetTheme(UiTheme.Dark);
            await WindowTestVisuals.UpdateLayoutAsync(dialog);

            Assert.Same(ownerFrame, WindowTestVisuals.Frame(owner));
            Assert.Same(dialogFrame, WindowTestVisuals.Frame(dialog));
            Assert.NotEqual(lightFrame, BrushColor(dialogFrame.Background));
            Assert.NotEqual(lightTitle, BrushColor(title.Foreground));
            Assert.NotEqual(lightOwner, BrushColor(ownerFrame.Background));
            Assert.Equal(ResourceColor("SurfaceBrush"), BrushColor(dialogFrame.Background));

            themes.SetTheme(UiTheme.Light);
            await WindowTestVisuals.UpdateLayoutAsync(dialog);
            Assert.Equal(lightFrame, BrushColor(dialogFrame.Background));
            Assert.Equal(lightTitle, BrushColor(title.Foreground));
        }
        finally
        {
            dialog.Close();
            owner.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>公共标题栏按钮需要在普通窗口上保留完整工作区并可重复还原。</summary>
    [Fact]
    public Task CaptionMaximizeAndRestore_KeepBaseWindowInsideWorkArea() => _fixture.RunAsync(async () =>
    {
        var content = new Grid();
        var window = CreateWindow(content);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Rect originalFrame = WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(window));
            Rect originalContent = WindowTestVisuals.ScreenBounds(content);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                var controls = Assert.Single(WindowTestVisuals.Descendants<WindowControls>(WindowTestVisuals.Caption(window)));
                var maximize = Assert.Single(WindowTestVisuals.Descendants<Button>(controls), item =>
                    item.Command == SystemCommands.MaximizeWindowCommand || item.Command == SystemCommands.RestoreWindowCommand);
                WindowTestVisuals.Invoke(maximize);
                await WindowTestVisuals.UpdateLayoutAsync(window);

                Assert.Equal(WindowState.Maximized, window.WindowState);
                Rect work = WindowBoundsNativeMethods.GetWorkArea(new WindowInteropHelper(window).Handle);
                WindowTestVisuals.AssertBounds(work, WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(window)),
                    "普通 HermodWindow 最大化时边框偏离显示器工作区。");
                var caption = WindowTestVisuals.Caption(window);
                Assert.Equal(42d, caption.ActualHeight, 3);
                double captionHeight = 42 * VisualTreeHelper.GetDpi(window).DpiScaleY;
                var body = new Rect(work.Left, work.Top + captionHeight, work.Width, work.Height - captionHeight);
                WindowTestVisuals.AssertBounds(body, WindowTestVisuals.ScreenBounds(content),
                    "公共窗口主体越过完整标题栏或显示器工作区。");

                WindowTestVisuals.Invoke(maximize);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Equal(WindowState.Normal, window.WindowState);
                WindowTestVisuals.AssertBounds(originalFrame, WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(window)),
                    "重复还原改变了公共窗口边框。");
                WindowTestVisuals.AssertBounds(originalContent, WindowTestVisuals.ScreenBounds(content),
                    "重复还原改变了公共窗口主体。");
            }
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>创建不可见但具有真实 HWND 的可缩放窗口。</summary>
    private static HermodWindow CreateWindow(FrameworkElement content) => new()
    {
        Title = "Window regression",
        Content = content,
        Width = 640,
        Height = 480,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = 100,
        Top = 100,
    };

    /// <summary>读取实际解析后的主题颜色，避免比较资源表达式本身。</summary>
    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    /// <summary>读取子窗的主题语义色，主体与拥有者可以使用不同背景层级。</summary>
    private static Color ResourceColor(string key) =>
        BrushColor(Assert.IsAssignableFrom<Brush>(Application.Current.FindResource(key)));
}
