using System.Windows;
using System.Windows.Input;

namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>远程连接的只读历史、粘贴确认和本机搜索不会产生隐式命令。</summary>
public sealed class TerminalRemoteInteractionTests
{
    /// <summary>断线后键盘和粘贴不发送字节，选区复制保持可用。</summary>
    [Fact]
    public void ReadOnlyBlocksTextAndPasteWhileKeepingSelection() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session, IsReadOnly = true };
        var inputs = new List<byte[]>(); session.InputReceived += (_, input) => inputs.Add(input.ToArray());
        session.FeedOutput("retained output"u8); control.PasteText("rm test\r");
        control.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, control, "text")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
        control.SelectAll(); Assert.Contains("retained output", control.GetSelectedText()); Assert.Empty(inputs);
    });

    /// <summary>取消粘贴在加括号和发送之前生效。</summary>
    [Fact]
    public void PasteConfirmationCanRejectWithoutSending() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session };
        int sent = 0; session.InputReceived += (_, _) => sent++;
        control.PreviewPaste += (_, args) => { Assert.Equal("one\ntwo", args.Text); args.Cancel = true; };
        control.PasteText("one\ntwo"); Assert.Equal(0, sent);
    });

    /// <summary>接受粘贴遵守远端已声明的 bracketed paste 模式。</summary>
    [Fact]
    public void AcceptedPasteUsesRemoteBracketedMode() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session };
        string? sent = null; session.InputReceived += (_, input) => sent = Encoding.UTF8.GetString(input.Span);
        session.FeedOutput("\x1b[?2004h"u8); control.PasteText("中文\nnext");
        Assert.Equal("\x1b[200~中文\nnext\x1b[201~", sent);
    });

    /// <summary>中文匹配以双宽单元格选择，查找和清理仅修改本机缓冲区。</summary>
    [Fact]
    public void SearchWideCharactersAndClearDisplayDoNotSend() => WpfTestThread.Run(() =>
    {
        using var session = new TerminalSession(); using var control = new TerminalControl { Session = session };
        int sent = 0; session.InputReceived += (_, _) => sent++;
        session.FeedOutput(Encoding.UTF8.GetBytes("中文 hello\r\n中文 next"));
        Assert.Equal((1, 2), control.FindText("中文")); Assert.Equal("中文", control.GetSelectedText());
        Assert.Equal((2, 2), control.FindText("中文")); Assert.Equal((1, 2), control.FindText("中文"));
        control.ClearDisplay(); Assert.Equal((0, 0), control.FindText("中文")); Assert.Equal(0, sent);
    });

    /// <summary>新的会话重置查找游标，不继续旧页签的匹配序号。</summary>
    [Fact]
    public void SearchRestartsWhenSessionChanges() => WpfTestThread.Run(() =>
    {
        using var first = new TerminalSession(); using var second = new TerminalSession(); using var control = new TerminalControl { Session = first };
        first.FeedOutput("match match"u8); second.FeedOutput("match match"u8);
        control.FindText("match"); Assert.Equal((2, 2), control.FindText("match"));
        control.Session = second; Assert.Equal((1, 2), control.FindText("match"));
    });
}
