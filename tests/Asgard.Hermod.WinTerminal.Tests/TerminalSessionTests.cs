namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>验证会话的输入输出方向、查询回复、尺寸通知与释放边界。</summary>
public sealed class TerminalSessionTests
{
    /// <summary>本地按键仅发送给连接器，不直接回显到屏幕，避免和 PTY 回显重复。</summary>
    [Fact]
    public void SendInput_DoesNotEchoLocallyAndOwnsInputBytes()
    {
        using var session = new TerminalSession();
        ReadOnlyMemory<byte> received = default;
        session.InputReceived += (_, bytes) => received = bytes;
        byte[] input = Encoding.UTF8.GetBytes("hello");
        session.SendInput(input);
        input[0] = (byte)'X';
        Assert.Equal("hello", Encoding.UTF8.GetString(received.Span));
        Assert.Equal(string.Empty, session.GetVisibleText().Trim());
    }

    /// <summary>来自 PTY 的光标位置查询通过同一输入通道返回正确回复。</summary>
    [Fact]
    public void CursorQuery_ReturnsReplyThroughInputChannel()
    {
        using var session = new TerminalSession();
        string? reply = null;
        session.InputReceived += (_, bytes) => reply = Encoding.UTF8.GetString(bytes.Span);
        session.FeedOutput("\x1b[5;9H\x1b[6n"u8);
        Assert.Equal("\x1b[5;9R", reply);
    }

    /// <summary>网格缩小时约束光标并通知 PTY 一次。</summary>
    [Fact]
    public void Resize_ClampsCursorAndNotifiesOnlyOnChange()
    {
        using var session = new TerminalSession();
        session.FeedOutput("\x1b[24;80H"u8);
        List<TerminalSize> notifications = [];
        session.SizeChanged += (_, size) => notifications.Add(size);
        session.Resize(40, 12);
        session.Resize(40, 12);
        Assert.Single(notifications);
        Assert.Equal(new TerminalSize(40, 12), notifications[0]);
        Assert.InRange(session.Decoder.State.CursorX, 0, 39);
        Assert.InRange(session.Decoder.State.CursorY, 0, 11);
    }

    /// <summary>远端终端缩小时，移出当前屏幕的输出仍可从历史中完整读取。</summary>
    [Fact]
    public void Resize_RemoteOutputRemainsInHistoryWhenViewportBecomesEmpty()
    {
        using var session = new TerminalSession(80, 24);
        session.FeedOutput("Connected\r\n你好 Hermod\r\n$ "u8);
        Assert.Contains("你好 Hermod", session.GetVisibleText());

        session.Resize(80, 12, pushScrollback: true);

        Assert.Empty(session.GetVisibleText());
        Assert.Contains("Connected", session.GetAllText());
        Assert.Contains("你好 Hermod", session.GetAllText());
        session.Resize(80, 24, pushScrollback: true);
        Assert.Contains("你好 Hermod", session.GetAllText());
    }

    /// <summary>释放可重复执行，释放后的读写和调整尺寸都明确失败。</summary>
    [Fact]
    public void DisposedSession_RejectsFurtherOperations()
    {
        var session = new TerminalSession();
        session.Dispose();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.FeedOutput("data"u8));
        Assert.Throws<ObjectDisposedException>(() => session.SendInput("data"u8));
        Assert.Throws<ObjectDisposedException>(() => session.Resize(40, 12));
        Assert.Throws<ObjectDisposedException>(() => session.GetVisibleText());
    }
}
