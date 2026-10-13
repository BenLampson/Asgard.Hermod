using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>数字键无需依赖输入法的文本提交，候选输入仍走 Unicode 提交且不本地回显。</summary>
public sealed class TerminalNumericInputTests
{
    /// <summary>两组数字键原样单次发送，前导零不丢失，也不被当成控制键。</summary>
    [Theory]
    [InlineData(Key.D0)]
    [InlineData(Key.NumPad0)]
    public void DigitsSendExactlyOnceWithoutTextComposition(Key first) => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session };
        using var source = new HwndSource(new HwndSourceParameters("Numeric input test") { Width = 100, Height = 100 });
        source.RootVisual = control;
        var sent = new List<string>(); session.InputReceived += (_, bytes) => sent.Add(Encoding.UTF8.GetString(bytes.Span));
        for (int digit = 0; digit < 10; digit++)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, first + digit) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            control.RaiseEvent(args); Assert.True(args.Handled);
        }
        Assert.Equal("0123456789", string.Concat(sent)); Assert.Equal(10, sent.Count);
        Assert.Empty(session.GetVisibleText().Trim());
    });

    /// <summary>组合中的数字留给输入法，候选提交只发送一次；结束组合后恢复数字输入。</summary>
    [Fact]
    public void CompositionKeepsCandidateDigitsAndCommitsUnicodeOnce() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session };
        using var source = new HwndSource(new HwndSourceParameters("IME input test") { Width = 100, Height = 100 }); source.RootVisual = control;
        var sent = new List<string>(); session.InputReceived += (_, bytes) => sent.Add(Encoding.UTF8.GetString(bytes.Span));
        var composition = new TextComposition(InputManager.Current, control, "中文123");
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputStartEvent });
        var digit = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.D1) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        control.RaiseEvent(digit); Assert.False(digit.Handled); Assert.Empty(sent);
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        Assert.Equal(["中文123"], sent);
        control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.D0) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.Equal(["中文123", "0"], sent);
    });

    /// <summary>断线后的数字和中文候选都不能发送到旧连接。</summary>
    [Fact]
    public void ReadOnlyRejectsDigitsAndComposition() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session, IsReadOnly = true };
        using var source = new HwndSource(new HwndSourceParameters("Readonly input test") { Width = 100, Height = 100 }); source.RootVisual = control;
        int sent = 0; session.InputReceived += (_, _) => sent++;
        control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.NumPad1) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, control, "123中文")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        Assert.Equal(0, sent);
    });
}
