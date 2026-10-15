using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>通过完整 WPF 输入流水线覆盖输入法接管数字后未提交文本的路径。</summary>
public sealed class TerminalImeNumericFallbackTests
{
    /// <summary>输入法接管数字却未提交文本时，主键盘和小键盘都必须保留输入。</summary>
    [Theory]
    [InlineData(Key.D0, false, false)]
    [InlineData(Key.D0, true, false)]
    [InlineData(Key.D0, false, true)]
    [InlineData(Key.NumPad0, false, false)]
    [InlineData(Key.NumPad0, true, false)]
    [InlineData(Key.NumPad0, false, true)]
    public void ClaimedDigitsWithoutTextStillSendExactlyOnce(Key first, bool emptyCommit, bool queued) => Run((terminal, source, sent) =>
    {
        foreach (char digit in "001111223344567899")
        {
            ProcessKey(terminal, source, first + digit - '0');
            if (emptyCommit) TextCompositionManager.StartComposition(new(InputManager.Current, terminal, ""));
            if (!queued) Drain();
            ProcessKey(terminal, source, first + digit - '0', release: true);
        }
        Drain();
        Assert.Equal("001111223344567899", string.Concat(sent));
        Assert.Equal(18, sent.Count);
    });

    /// <summary>真实 TSF 可在 PreviewKeyDown 路由前同步提交数字；连续和长按都不能重复发送。</summary>
    [Theory]
    [InlineData(Key.D0)]
    [InlineData(Key.NumPad0)]
    public void TextBeforeRoutedKeySendsRepeatedDigitsExactlyOnce(Key first) => Run((terminal, source, sent) =>
    {
        Keyboard.ClearFocus();
        void CommitBeforeRoute(object sender, PreProcessInputEventArgs args)
        {
            if (args.StagingItem.Input is KeyEventArgs key && key.RoutedEvent == Keyboard.PreviewKeyDownEvent)
                TextCompositionManager.StartComposition(new(InputManager.Current, terminal,
                    ((char)('0' + key.ImeProcessedKey - first)).ToString()));
        }
        // WPF 按逆注册顺序调用预处理；模拟输入法先注册，控件随后获得焦点。
        InputManager.Current.PreProcessInput += CommitBeforeRoute;
        try
        {
            Assert.True(terminal.Focus());
            foreach (char digit in "001111223344567899")
                ProcessKey(terminal, source, first + digit - '0');
            Drain();
            Assert.Equal("001111223344567899", string.Concat(sent));
            Assert.Equal(18, sent.Count);
        }
        finally { InputManager.Current.PreProcessInput -= CommitBeforeRoute; }
    });

    /// <summary>下一按键先落实未提交的数字，不能把数字延后到随后字母的文本之后。</summary>
    [Fact]
    public void MissingDigitStaysBeforeFollowingLetter() => Run((terminal, source, sent) =>
    {
        ProcessKey(terminal, source, Key.D1);
        ProcessKey(terminal, source, Key.T);
        TextCompositionManager.StartComposition(new(InputManager.Current, terminal, "t"));
        Drain();
        Assert.Equal(["1", "t"], sent);
    });

    /// <summary>释放按键先结束旧兜底和去重，下一次独立文本不能吞掉旧数字或被旧数字吞掉。</summary>
    [Fact]
    public void KeyReleaseKeepsFollowingIndependentText() => Run((terminal, source, sent) =>
    {
        ProcessKey(terminal, source, Key.D1);
        ProcessKey(terminal, source, Key.D1, release: true);
        TextCompositionManager.StartComposition(new(InputManager.Current, terminal, "1"));
        Drain();
        Assert.Equal(["1", "1"], sent);
    });

    /// <summary>按键后的候选开始通知也能取消兜底，空提交结束组合不能泄漏候选数字。</summary>
    [Fact]
    public void LaterCandidateStartCancelsFallbackEvenWhenCanceled() => Run((terminal, source, sent) =>
    {
        ProcessKey(terminal, source, Key.D1);
        var composition = new TextComposition(InputManager.Current, terminal, "", TextCompositionAutoComplete.Off);
        TextCompositionManager.StartComposition(composition);
        TextCompositionManager.CompleteComposition(composition);
        Drain();
        Assert.Empty(sent);
    });

    /// <summary>文本在按键路由后提交时，应取消延后兜底，不能发送两个相同数字。</summary>
    [Fact]
    public void CommittedTextCancelsDeferredFallback() => Run((terminal, source, sent) =>
    {
        ProcessKey(terminal, source, Key.D1);
        TextCompositionManager.StartComposition(new(InputManager.Current, terminal, "1"));
        Drain();
        Assert.Equal(["1"], sent);
    });

    /// <summary>已有候选的选词数字不得在组合结束后作为延后的数字泄漏到远端。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("中文")]
    public void CandidateDigitsNeverBecomeFallbackInput(string committedText) => Run((terminal, source, sent) =>
    {
        var composition = new TextComposition(InputManager.Current, terminal, committedText, TextCompositionAutoComplete.Off);
        TextCompositionManager.StartComposition(composition);
        ProcessKey(terminal, source, Key.D1);
        TextCompositionManager.CompleteComposition(composition);
        Drain();
        Assert.Equal(committedText.Length == 0 ? [] : new[] { committedText }, sent);
    });

    /// <summary>排队的数字兜底必须遵守焦点、只读和会话边界。</summary>
    [Theory]
    [InlineData("focus")]
    [InlineData("readonly")]
    [InlineData("session")]
    [InlineData("unload")]
    public void ContextChangeCancelsDeferredFallback(string boundary) => Run((terminal, source, sent) =>
    {
        ProcessKey(terminal, source, Key.D1);
        using var replacement = new TerminalSession();
        switch (boundary)
        {
            case "focus": Keyboard.ClearFocus(); break;
            case "readonly": terminal.IsReadOnly = true; break;
            case "session": terminal.Session = replacement; break;
            case "unload": terminal.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
        }
        Drain();
        Assert.Empty(sent);
    });

    /// <summary>只有输入法标记使用反射，按键和文本都经过 InputManager 的真实预处理及后处理。</summary>
    private static void ProcessKey(TerminalControl terminal, PresentationSource source, Key key, bool release = false)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        { RoutedEvent = release ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent, Source = terminal };
        typeof(KeyEventArgs).GetMethod("MarkImeProcessed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(args, null);
        InputManager.Current.ProcessInput(args);
    }

    /// <summary>排空当前输入事件之后的 Dispatcher 工作，不依赖固定等待时间。</summary>
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>隐藏测试窗口只承载本机输入和字节观察，不连接 Shell 或服务器。</summary>
    private static void Run(Action<TerminalControl, PresentationSource, List<string>> action) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var terminal = new TerminalControl { Session = session };
        InputMethod.SetIsInputMethodEnabled(terminal, false);
        var sent = new List<string>();
        session.InputReceived += (_, bytes) => sent.Add(Encoding.UTF8.GetString(bytes.Span));
        var window = new Window { Content = terminal, Width = 400, Height = 200, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            window.Show();
            Assert.True(terminal.Focus());
            action(terminal, PresentationSource.FromVisual(terminal)!, sent);
        }
        finally { window.Close(); }
    });
}
