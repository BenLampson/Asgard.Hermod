using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>手工路由输入与直接坐标驱动；不创建 HWND、不访问桌面焦点和指针。</summary>
public sealed class TerminalHeadlessInteractionTests
{
    [Theory]
    [InlineData(Key.D0)]
    [InlineData(Key.NumPad0)]
    public void AutoCompleteStartWithoutCommitCannotLockOutDigits(Key first) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session };
        var source = new NoWindowSource { RootVisual = control }; var sent = Capture(session);
        Start(control, TextCompositionAutoComplete.On);
        for (int digit = 0; digit < 10; digit++)
        {
            var args = Press(control, source, first + digit); Assert.True(args.Handled);
            Submit(control, digit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Assert.Equal("0123456789", string.Concat(sent)); Assert.Equal(10, sent.Count);
        Assert.Null(PresentationSource.FromVisual(control));
    });

    [Fact]
    public void CandidateCommitEmptyCancellationAndEscapeRestoreDigits() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session };
        var source = new NoWindowSource { RootVisual = control }; var sent = Capture(session);
        Start(control, TextCompositionAutoComplete.Off);
        Assert.False(Press(control, source, Key.D1).Handled); Assert.False(Press(control, source, Key.NumPad2).Handled); Assert.Empty(sent);
        Submit(control, "中文1"); Assert.Equal(["中文1"], sent);
        Start(control, TextCompositionAutoComplete.Off); Submit(control, ""); Press(control, source, Key.NumPad1);
        Start(control, TextCompositionAutoComplete.Off); Assert.False(Press(control, source, Key.Escape).Handled); Press(control, source, Key.NumPad2);
        Assert.Equal(["中文1", "1", "2"], sent);
    });

    [Fact]
    public void SelectionStartsOnlyAfterDragThresholdAndBlankDragLeavesNothing() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); session.FeedOutput("abcdef"u8);
        using var control = new TerminalControl { Session = session };
        Call(control, "BeginSelection", new Point(0, 0)); Assert.Empty(control.GetSelectedText());
        Call(control, "ExtendSelection", new Point(SystemParameters.MinimumHorizontalDragDistance / 2, 0)); Assert.Empty(control.GetSelectedText());
        Call(control, "EndSelection"); Assert.Empty(control.GetSelectedText());
        Call(control, "BeginSelection", new Point(0, 0)); Call(control, "ExtendSelection", new Point(60, 0));
        Call(control, "EndSelection"); Assert.Equal("abcdef", control.GetSelectedText());
        control.ClearSelection(); Assert.Empty(control.GetSelectedText());
        Call(control, "BeginSelection", new Point(300, 0)); Call(control, "ExtendSelection", new Point(400, 0)); Call(control, "EndSelection");
        Assert.Empty(control.GetSelectedText());
        Assert.Null(typeof(TerminalControl).GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control));
    });

    [Fact]
    public void ClipboardCommandsUseRealSelectionAndPasteConfirmationWithoutClipboardAccess() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); session.FeedOutput("existing text"u8);
        using var control = new TerminalControl { Session = session }; var sent = Capture(session);
        Assert.False(ApplicationCommands.Copy.CanExecute(null, control));
        ApplicationCommands.SelectAll.Execute(null, control);
        Assert.True(ApplicationCommands.Copy.CanExecute(null, control)); Assert.Contains("existing text", control.GetSelectedText(), StringComparison.Ordinal);
        control.PreviewPaste += (_, args) => args.Cancel = true;
        control.PasteText("first\nsecond"); Assert.Empty(sent);
        control.ClearSelection(); Assert.False(ApplicationCommands.Copy.CanExecute(null, control));
        control.IsReadOnly = true; Assert.False(ApplicationCommands.Paste.CanExecute(null, control));
    });

    private static List<string> Capture(TerminalSession session)
    {
        var sent = new List<string>(); session.InputReceived += (_, value) => sent.Add(Encoding.UTF8.GetString(value.Span)); return sent;
    }
    private static KeyEventArgs Press(TerminalControl control, PresentationSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        control.RaiseEvent(args); return args;
    }
    private static void Start(TerminalControl control, TextCompositionAutoComplete mode) => control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
        new TextComposition(InputManager.Current, control, "candidate", mode))
    { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
    private static void Submit(TerminalControl control, string text) => control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
        new TextComposition(InputManager.Current, control, text))
    { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
    private static void Call(TerminalControl control, string method, params object[] arguments) =>
        typeof(TerminalControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, arguments);

    /// <summary>只满足 KeyEventArgs 的来源契约，不注册原生呈现目标。</summary>
    private sealed class NoWindowSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
