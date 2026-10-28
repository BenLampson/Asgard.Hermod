using System.Runtime.InteropServices;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Asgard.Hermod.MySql.UI.Views;
using Asgard.Hermod.PostgreSql.UI.Views;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用实际布局命中和 WPF 鼠标路由验证输入区，不移动桌面指针。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentPanelPointerTests(WpfTestFixture fixture)
{
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint value, nint state);

    /// <summary>编辑器整面可命中，外框留白可获得焦点并输入，窄窗与宽窗行为一致。</summary>
    [Theory]
    [InlineData("ssh", 1120)]
    [InlineData("ssh", 460)]
    [InlineData("mysql", 460)]
    [InlineData("postgresql", 460)]
    public Task BlankComposerAreasFocusThePrompt(string kind, double width) => fixture.RunAsync(async () =>
    {
        UserControl view = kind switch { "ssh" => new SshAssistantView(), "mysql" => new MySqlAssistantView(), _ => new PostgreSqlAssistantView() };
        var model = new AgentPanelTestModel();
        view.DataContext = model;
        var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
        var window = new HermodWindow { Content = view, Width = width, Height = 780, ShowInTaskbar = false, ShowActivated = true, Opacity = 0 };
        InputMethod.SetIsInputMethodEnabled(window, false);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            window.Activate();
            await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            var composer = Assert.IsType<Border>(panel.FindName("Composer"));
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            var toggle = Assert.IsType<ToggleButton>(panel.FindName("ContextToggle"));
            // 文本宿主覆盖整个编辑区，命中后由原生 TextBox 处理光标与拖选。
            foreach (string draft in new[] { "", "draft", string.Join('\n', Enumerable.Repeat("long draft", 30)) })
            {
                model.Prompt = draft;
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Point[] editorPoints =
                [
                    new(1, 1),
                    new(prompt.ActualWidth * 0.6, prompt.ActualHeight * 0.7),
                    new(prompt.ActualWidth - 2, prompt.ActualHeight - 2),
                ];
                foreach (Point point in editorPoints)
                {
                    var hit = Assert.IsAssignableFrom<DependencyObject>(composer.InputHitTest(prompt.TranslatePoint(point, composer)));
                    Assert.True(IsWithin(hit, prompt), $"Point={point}; Hit={hit}");
                }
            }
            model.Prompt = "";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Point[] framePoints =
            [
                new Point(7, 7),
                new Point(composer.ActualWidth - 7, composer.ActualHeight / 2),
                new Point(composer.ActualWidth * 0.65, composer.ActualHeight - 7),
            ];
            foreach (Point point in framePoints)
            {
                Assert.True(toggle.Focus());
                await ClickAsync(composer, point);
                Assert.True(prompt.IsKeyboardFocusWithin, $"Point={point}; {WindowInteractionNativeMethods.DescribeFocus(window, prompt)}");
                await TypeAsync(window, 'a');
                Assert.EndsWith("a", model.Prompt, StringComparison.OrdinalIgnoreCase);
                model.Prompt = "";
                await WindowTestVisuals.UpdateLayoutAsync(window);
            }
            // 等待回复时仍可编辑草稿；点击留白会收起旧选区，避免误替换所选文本。
            model.IsBusy = true;
            model.Prompt = "HELLO";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            prompt.Select(1, 3);
            Assert.True(toggle.Focus());
            await ClickAsync(composer, framePoints[0]);
            Assert.True(prompt.IsKeyboardFocusWithin);
            Assert.Equal(0, prompt.SelectionLength);
            await TypeAsync(window, 'a');
            Assert.Equal("HELLO", model.Prompt.Replace("a", "", StringComparison.Ordinal));
            panel.IsSourceClosed = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(prompt.IsReadOnly);
            string closedDraft = model.Prompt;
            Assert.True(toggle.Focus());
            await ClickAsync(composer, framePoints[0]);
            await TypeAsync(window, 'x');
            Assert.Equal(closedDraft, model.Prompt);
        }
        finally { window.Close(); }
    });

    /// <summary>底部和展开的上下文控件保留自己的命中、焦点与操作。</summary>
    [Fact]
    public Task ComposerControlsKeepTheirOwnInteraction() => fixture.RunAsync(async () =>
    {
        var model = new AgentPanelTestModel { Prompt = "draft" };
        var view = new MySqlAssistantView { DataContext = model };
        var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
        var window = new HermodWindow { Content = view, Width = 640, Height = 780, ShowInTaskbar = false, ShowActivated = true, Opacity = 0 };
        InputMethod.SetIsInputMethodEnabled(window, false);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            window.Activate();
            await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            var composer = Assert.IsType<Border>(panel.FindName("Composer"));
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            var toggle = Assert.IsType<ToggleButton>(panel.FindName("ContextToggle"));
            Assert.True(toggle.Focus());
            Point togglePoint = toggle.TranslatePoint(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2), composer);
            Assert.True(IsWithin(Assert.IsAssignableFrom<DependencyObject>(composer.InputHitTest(togglePoint)), toggle));
            await ClickAsync(composer, togglePoint);
            Assert.False(prompt.IsKeyboardFocusWithin);
            var togglePeer = new ToggleButtonAutomationPeer(toggle);
            Assert.IsAssignableFrom<IToggleProvider>(togglePeer.GetPattern(PatternInterface.Toggle)).Toggle();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(toggle.IsChecked);
            var check = WindowTestVisuals.Descendants<CheckBox>(panel).First(item => item.IsEnabled);
            Point checkPoint = check.TranslatePoint(new Point(check.ActualWidth / 2, check.ActualHeight / 2), composer);
            Assert.True(IsWithin(Assert.IsAssignableFrom<DependencyObject>(composer.InputHitTest(checkPoint)), check));
            await ClickAsync(composer, checkPoint);
            Assert.False(prompt.IsKeyboardFocusWithin);
            bool before = check.IsChecked == true;
            Assert.IsAssignableFrom<IToggleProvider>(new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle)).Toggle();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.NotEqual(before, check.IsChecked == true);
            model.IsBusy = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var stop = Assert.Single(WindowTestVisuals.Descendants<Button>(panel), button => ReferenceEquals(button.Command, model.CancelCommand));
            Point stopPoint = stop.TranslatePoint(new Point(stop.ActualWidth / 2, stop.ActualHeight / 2), composer);
            Assert.True(IsWithin(Assert.IsAssignableFrom<DependencyObject>(composer.InputHitTest(stopPoint)), stop));
            await ClickAsync(composer, stopPoint);
            Assert.False(prompt.IsKeyboardFocusWithin);
            WindowTestVisuals.Invoke(stop);
            Assert.Equal(1, model.CancelCount);
        }
        finally { window.Close(); }
    });

    /// <summary>使用真实独立对话窗口验证缩放后的命中、输入和 Tab 导航。</summary>
    [Fact]
    public Task ChatWindowKeepsComposerUsableAfterResizing() => fixture.RunAsync(async () =>
    {
        var model = new AgentPanelTestModel();
        var view = new SshAssistantView();
        var request = new AgentWindowRequest(new object(), model, "SSH", "host", "terminal", () => view, () => { }, () => false);
        var window = new AgentChatWindow(request) { Width = 1120, Height = 780, ShowInTaskbar = false, ShowActivated = true, Opacity = 0 };
        InputMethod.SetIsInputMethodEnabled(window, false);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            window.Activate();
            await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
            var composer = Assert.IsType<Border>(panel.FindName("Composer"));
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            var toggle = Assert.IsType<ToggleButton>(panel.FindName("ContextToggle"));
            double initialWidth = composer.ActualWidth;
            window.Width = 640;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(composer.ActualWidth < initialWidth);
            Point editorPoint = prompt.TranslatePoint(new Point(prompt.ActualWidth / 2, prompt.ActualHeight * 0.75), composer);
            Assert.True(IsWithin(Assert.IsAssignableFrom<DependencyObject>(composer.InputHitTest(editorPoint)), prompt));
            Assert.True(toggle.Focus());
            await ClickAsync(composer, new Point(7, 7));
            Assert.True(prompt.IsKeyboardFocusWithin);
            await TypeAsync(window, 'a');
            Assert.Equal("a", model.Prompt);
            Assert.True(prompt.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.False(prompt.IsKeyboardFocusWithin);
        }
        finally { window.ClosePermanently(); }
    });

    /// <summary>按实际布局命中目标，再沿 WPF 鼠标事件路由发送按下事件。</summary>
    private static async Task ClickAsync(FrameworkElement composer, Point point)
    {
        var target = Assert.IsAssignableFrom<UIElement>(composer.InputHitTest(point));
        var preview = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        }
        await WpfTestFixture.DrainDispatcherAsync();
    }

    /// <summary>字符消息只发送给测试窗口，验证获得焦点后的真实文本输入和双向绑定。</summary>
    private static async Task TypeAsync(Window window, char character)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        Assert.True(PostMessage(handle, 0x0102, character, 1));
        await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
        await WindowTestVisuals.UpdateLayoutAsync(window);
    }

    /// <summary>验证命中目标属于输入控件，而不是后面的面板背景。</summary>
    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
}
