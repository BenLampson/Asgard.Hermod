using System.Runtime.InteropServices;
using System.Windows.Input;
using Asgard.Hermod.MySql.UI.Views;
using Asgard.Hermod.PostgreSql.UI.Views;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>仅向测试 HWND 发送按键，验证三个模块统一的输入、焦点和消息生命周期。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentPanelKeyboardTests(WpfTestFixture fixture)
{
    /// <summary>读取当前测试线程的键盘状态，测试结束后原样恢复。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] state);

    /// <summary>只修改测试线程的修饰键状态，不生成桌面全局按键。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);

    /// <summary>建议卡片使用真实键盘路由展开与收起，窄窗缩放不引入内层纵向滚动。</summary>
    [Fact]
    public Task SuggestionCanBeCollapsedAndExpandedFromKeyboard() => fixture.RunAsync(async () =>
    {
        var card = new AgentCodeSuggestion { Caption = "test", Code = string.Join('\n', Enumerable.Repeat("SELECT 1;", 40)) };
        var scroll = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var window = new HermodWindow { Content = scroll, Width = 640, Height = 600, ShowInTaskbar = false, ShowActivated = true, Opacity = 0 };
        InputMethod.SetIsInputMethodEnabled(window, false);
        try
        {
            window.Show(); await WindowTestVisuals.UpdateLayoutAsync(window);
            var toggle = (System.Windows.Controls.Primitives.ToggleButton)card.FindName("SuggestionToggle");
            Assert.True(toggle.Focus()); await PressEnterAsync(window, ModifierKeys.None); Assert.False(card.IsExpanded);
            await WindowInteractionNativeMethods.PressKeyAsync(window, 0x20); await WindowTestVisuals.UpdateLayoutAsync(window); Assert.True(card.IsExpanded);
            window.Width = 360; await WindowTestVisuals.UpdateLayoutAsync(window);
            var code = (TextBox)card.FindName("SuggestionCode"); Assert.True(code.ActualHeight > 180); Assert.True(scroll.ScrollableHeight > 0);
            Assert.InRange(code.ExtentHeight - code.ViewportHeight, -1, 1);
            scroll.ScrollToEnd(); await WindowTestVisuals.UpdateLayoutAsync(window);
            var collapse = (Button)card.FindName("CollapseSuggestion"); Assert.True(collapse.Focus());
            await PressEnterAsync(window, ModifierKeys.None); Assert.False(card.IsExpanded); Assert.True(toggle.IsKeyboardFocused);
        }
        finally { window.Close(); }
    });

    /// <summary>Enter、Ctrl+Enter 和 Shift+Enter 共用实际路由；切换上下文及收起不丢草稿或抢焦点。</summary>
    [Theory]
    [InlineData("ssh")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    public Task KeyboardAndCollectionLifecycleStayInsideTheSharedPanel(string kind) => fixture.RunAsync(async () =>
    {
        UserControl view = kind switch { "ssh" => new SshAssistantView(), "mysql" => new MySqlAssistantView(), _ => new PostgreSqlAssistantView() };
        var model = new AgentPanelTestModel { Prompt = "draft" };
        view.DataContext = model;
        var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
        var window = new HermodWindow { Content = view, Width = 460, Height = 780, ShowInTaskbar = false, ShowActivated = true, Opacity = 0 };
        InputMethod.SetIsInputMethodEnabled(window, false);
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            Assert.True(prompt.Focus());
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(prompt.IsKeyboardFocusWithin, WindowInteractionNativeMethods.DescribeFocus(window, prompt));
            await PressEnterAsync(window, ModifierKeys.None);
            Assert.Equal(1, model.SendCount);
            Assert.Equal("draft", model.Prompt);
            await PressEnterAsync(window, ModifierKeys.Control);
            Assert.Equal(2, model.SendCount);
            // 上下文默认折叠，先通过真实入口展开再验证其中控件的键盘边界。
            ((System.Windows.Controls.Primitives.ToggleButton)panel.FindName("ContextToggle")).IsChecked = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(WindowTestVisuals.Descendants<CheckBox>(panel).First().Focus());
            await PressEnterAsync(window, ModifierKeys.None);
            Assert.Equal(2, model.SendCount);
            Assert.True(prompt.Focus());
            await PressEnterAsync(window, ModifierKeys.Shift);
            Assert.Equal(2, model.SendCount);
            Assert.Contains('\n', model.Prompt);
            model.IsBusy = true;
            string busyDraft = model.Prompt;
            await PressEnterAsync(window, ModifierKeys.None);
            Assert.Equal(2, model.SendCount);
            Assert.Equal(busyDraft, model.Prompt);
            model.IsBusy = false;
            Assert.True(prompt.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.False(prompt.IsKeyboardFocusWithin);
            Assert.True(window.IsKeyboardFocusWithin);
            Assert.True(prompt.Focus());

            var scroll = Assert.IsType<ScrollViewer>(panel.FindName("ConversationScroll"));
            for (int index = 0; index < 20; index++) model.Messages.Add(Message(kind, "Reply " + index));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(scroll.ScrollableHeight > 0);
            Assert.True(scroll.VerticalOffset > 0);
            Assert.True(prompt.IsKeyboardFocusWithin);
            // 阅读旧消息时新回复不能抢回底部；回到末尾后继续跟随。
            scroll.ScrollToVerticalOffset(40);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            double readingOffset = scroll.VerticalOffset;
            model.Messages.Add(Message(kind, "reply while reading history"));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(readingOffset, scroll.VerticalOffset, 1);
            scroll.ScrollToEnd();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            model.Messages.Add(Message(kind, "reply while following the end"));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset, 1);
            view.Visibility = Visibility.Collapsed;
            model.Messages.Add(Message(kind, "hidden reply"));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(busyDraft, model.Prompt);
            view.Visibility = Visibility.Visible;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(busyDraft, prompt.Text);

            var replacement = new AgentPanelTestModel { Prompt = "other workspace" };
            view.DataContext = replacement;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(replacement.Messages, panel.Messages);
            Assert.Equal("other workspace", prompt.Text);
            model.Messages.Add(Message(kind, "old workspace reply"));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Empty(replacement.Messages);
            Assert.Equal("other workspace", prompt.Text);
            window.Width = 320;
            await WindowPreviewCapture.VerifyAsync(window, $"agent-shared-{kind}-compact");
        }
        finally { window.Close(); }
    });

    /// <summary>为实际模块模板提供对应类型，避免通过本地化角色名称判断来源。</summary>
    private static object Message(string kind, string body) => kind switch
    {
        "ssh" => new Asgard.Hermod.Ssh.UI.ViewModels.SshAssistantMessage(body, false, 0),
        "mysql" => new Asgard.Hermod.MySql.UI.ViewModels.MySqlAssistantMessage(body, false),
        _ => new Asgard.Hermod.PostgreSql.UI.ViewModels.PostgreSqlAssistantMessage(body, false),
    };

    /// <summary>修饰键只属于测试线程，按键消息只发送到指定测试窗口，不触及终端或真实数据库。</summary>
    private static async Task PressEnterAsync(HermodWindow window, ModifierKeys modifiers)
    {
        var previous = new byte[256];
        Assert.True(GetKeyboardState(previous));
        var state = (byte[])previous.Clone();
        state[0x10] = modifiers.HasFlag(ModifierKeys.Shift) ? (byte)0x80 : (byte)0;
        state[0x11] = modifiers.HasFlag(ModifierKeys.Control) ? (byte)0x80 : (byte)0;
        // WPF 逐一查询左右修饰键，不能只设置 Win32 的合并状态位。
        state[0xA0] = state[0x10]; state[0xA1] = 0;
        state[0xA2] = state[0x11]; state[0xA3] = 0;
        try
        {
            Assert.True(SetKeyboardState(state));
            Assert.Equal(modifiers, Keyboard.Modifiers);
            await WindowInteractionNativeMethods.PressKeyAsync(window, 0x0D);
            await WindowTestVisuals.UpdateLayoutAsync(window);
        }
        finally { Assert.True(SetKeyboardState(previous)); }
    }
}
