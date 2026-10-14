using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>验证数字键与文本提交的协调，不吞掉连续数字或输入法候选。</summary>
public sealed class TerminalNumericInputDeduplicationTests
{
    /// <summary>两组数字键即使还产生自动文本组合，也只向后端发送一次。</summary>
    [Theory]
    [InlineData(Key.D0, false)]
    [InlineData(Key.D0, true)]
    [InlineData(Key.NumPad0, false)]
    [InlineData(Key.NumPad0, true)]
    public void DigitsSendOnceWhenKeyAndTextEventsArrive(Key first, bool startComposition) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        using var source = CreateSource(control);
        var sent = CaptureInput(session);
        for (int digit = 0; digit < 10; digit++)
        {
            Assert.True(RaiseKey(control, source, first + digit).Handled);
            SubmitText(control, digit.ToString(System.Globalization.CultureInfo.InvariantCulture), startComposition);
            RaiseKey(control, source, first + digit, release: true);
        }
        Assert.Equal("0123456789", string.Concat(sent));
        Assert.Equal(10, sent.Count);
        Assert.Empty(session.GetVisibleText().Trim());
    });

    /// <summary>每次物理按键各发送一次，文本事件缺失或交替到达都不吞连续相同数字。</summary>
    [Theory]
    [InlineData(Key.D0, 0)]
    [InlineData(Key.D0, 1)]
    [InlineData(Key.D0, 2)]
    [InlineData(Key.NumPad0, 0)]
    [InlineData(Key.NumPad0, 1)]
    [InlineData(Key.NumPad0, 2)]
    public void RepeatedDigitsRemainDistinctWhenTextEventsAreOptional(Key first, int textPattern) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        using var source = CreateSource(control);
        var sent = CaptureInput(session);
        const string digits = "001111223344567899";
        for (int index = 0; index < digits.Length; index++)
        {
            Key key = first + digits[index] - '0';
            Assert.True(RaiseKey(control, source, key).Handled);
            if (textPattern == 1 || textPattern == 2 && index % 2 == 0)
                SubmitText(control, digits[index].ToString(), startComposition: true);
            // 不插入 KeyUp，同时覆盖按住数字键时系统产生的重复 KeyDown。
        }
        Assert.Equal(digits, string.Concat(sent));
        Assert.Equal(digits.Length, sent.Count);
    });

    /// <summary>输入法已认领的按键即使尚未通知组合开始，也不能由终端提前发送或拦截。</summary>
    [Theory]
    [InlineData(Key.D1)]
    [InlineData(Key.NumPad1)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Left)]
    [InlineData(Key.Down)]
    public void ImeOwnedKeysWaitForCommittedText(Key key) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        using var source = CreateSource(control);
        var sent = CaptureInput(session);
        var args = RaiseKey(control, source, key, imeProcessed: true);
        Assert.False(args.Handled);
        Assert.Empty(sent);
        SubmitText(control, "中文1", startComposition: true);
        Assert.Equal(["中文1"], sent);
    });

    /// <summary>新的候选组合可以提交与上一数字相同的文本，候选数字、回车和方向键不发送。</summary>
    [Theory]
    [InlineData("1", false)]
    [InlineData("1", true)]
    [InlineData("中文123", false)]
    [InlineData("中文123", true)]
    public void CandidateCompositionDoesNotInheritNumericSuppression(string committedText, bool useCandidateKeys) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        using var source = CreateSource(control);
        var sent = CaptureInput(session);
        RaiseKey(control, source, Key.D1);
        var composition = new TextComposition(InputManager.Current, control, committedText, TextCompositionAutoComplete.Off);
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        if (useCandidateKeys)
        {
            foreach (var key in new[] { Key.D1, Key.NumPad1, Key.Enter, Key.Left, Key.Down })
            {
                Assert.False(RaiseKey(control, source, key).Handled);
                Assert.Equal(["1"], sent);
            }
        }
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        Assert.Equal(["1", committedText], sent);
        RaiseKey(control, source, Key.D1);
        SubmitText(control, "1", startComposition: true);
        Assert.Equal(["1", committedText, "1"], sent);
    });

    /// <summary>只拦截与数字键完全一致的文本，完整中文、全角数字和多字符提交仍原样发送。</summary>
    [Theory]
    [InlineData("中文123")]
    [InlineData("１")]
    [InlineData("11")]
    public void DifferentCommittedTextIsNotSuppressed(string text) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        using var source = CreateSource(control);
        var sent = CaptureInput(session);
        RaiseKey(control, source, Key.D1);
        SubmitText(control, text);
        Assert.Equal(["1", text], sent);
    });

    /// <summary>按键释放或后续按键会结束去重范围，不误吞下一次独立的文本输入。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NextKeyboardEventEndsNumericSuppression(bool release) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        using var source = CreateSource(control);
        var sent = CaptureInput(session);
        RaiseKey(control, source, Key.D1);
        RaiseKey(control, source, release ? Key.D1 : Key.A, release: release);
        SubmitText(control, "1");
        Assert.Equal(["1", "1"], sent);
    });

    /// <summary>焦点、挂载和只读状态的变化不能保留旧按键的去重或候选组合状态。</summary>
    [Theory]
    [InlineData("focus", false)]
    [InlineData("focus", true)]
    [InlineData("unload", false)]
    [InlineData("unload", true)]
    [InlineData("readonly", false)]
    [InlineData("readonly", true)]
    public void InputLifecycleEndsNumericSuppression(string boundary, bool composing) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        using var source = CreateSource(control);
        var sent = CaptureInput(session);
        RaiseKey(control, source, Key.D1);
        if (composing)
        {
            var composition = new TextComposition(InputManager.Current, control, "候选", TextCompositionAutoComplete.Off);
            control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        }
        switch (boundary)
        {
            case "focus":
                typeof(TerminalControl).GetMethod("OnIsKeyboardFocusWithinChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(control, [new DependencyPropertyChangedEventArgs(UIElement.IsKeyboardFocusWithinProperty, true, false)]);
                break;
            case "unload":
                control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                break;
            case "readonly":
                control.IsReadOnly = true;
                control.IsReadOnly = false;
                break;
        }
        if (composing)
            Assert.True(RaiseKey(control, source, Key.D1).Handled);
        SubmitText(control, "1");
        Assert.Equal(["1", "1"], sent);
    });

    /// <summary>替换会话后，不沿用旧会话的数字去重或输入法组合状态。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewSessionStartsWithFreshInputState(bool composing) => WpfTestThread.Run(() =>
    {
        using var first = new TerminalSession();
        using var second = new TerminalSession();
        using var control = new TerminalControl { Session = first };
        using var source = CreateSource(control);
        var sent = CaptureInput(second);
        RaiseKey(control, source, Key.D1);
        if (composing)
        {
            var composition = new TextComposition(InputManager.Current, control, "候选", TextCompositionAutoComplete.Off);
            control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        }
        control.Session = second;
        if (composing)
            Assert.True(RaiseKey(control, source, Key.D1).Handled);
        SubmitText(control, "1");
        Assert.Equal(["1"], sent);
    });

    /// <summary>没有按键事件的 Unicode 提交仍原样发送，重复数字和粘贴不会被全局去重。</summary>
    [Fact]
    public void TextOnlyAndPastedDigitsRemainUnchanged() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession();
        using var control = new TerminalControl { Session = session };
        var sent = CaptureInput(session);
        SubmitText(control, "11", startComposition: true);
        SubmitText(control, "1", startComposition: true);
        control.PasteText("111中文");
        Assert.Equal(["11", "1", "111中文"], sent);
        Assert.Empty(session.GetVisibleText().Trim());
    });

    /// <summary>创建不可见的 WPF 输入宿主，不改变用户桌面的焦点。</summary>
    private static HwndSource CreateSource(TerminalControl control)
    {
        var source = new HwndSource(new HwndSourceParameters("Numeric input coordination test")
        { Width = 100, Height = 100, WindowStyle = 0 });
        source.RootVisual = control;
        return source;
    }

    /// <summary>直接观察控件发送的 UTF-8 字节，不依赖 Shell 的回显行为。</summary>
    private static List<string> CaptureInput(TerminalSession session)
    {
        var sent = new List<string>();
        session.InputReceived += (_, bytes) => sent.Add(Encoding.UTF8.GetString(bytes.Span));
        return sent;
    }

    /// <summary>模拟 WPF 按键事件；反射仅用于重现框架内部设置的输入法归属标记。</summary>
    private static KeyEventArgs RaiseKey(TerminalControl control, HwndSource source, Key key, bool release = false, bool imeProcessed = false)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        { RoutedEvent = release ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent };
        if (imeProcessed)
            typeof(KeyEventArgs).GetMethod("MarkImeProcessed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(args, null);
        control.RaiseEvent(args);
        return args;
    }

    /// <summary>模拟普通字符的自动组合，可重现数字 KeyDown 后仍收到文本提交的输入路径。</summary>
    private static void SubmitText(TerminalControl control, string text, bool startComposition = false)
    {
        var composition = new TextComposition(InputManager.Current, control, text);
        if (startComposition)
            control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
    }
}
