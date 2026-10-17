namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>验证字节分块、颜色、中文列宽与屏幕模式的外部行为。</summary>
public sealed class TerminalParsingTests
{
    /// <summary>中文 UTF-8 每个字节单独到达也应保持两个字符及其双列布局。</summary>
    [Fact]
    public void FeedOutput_PreservesChineseAcrossSingleByteChunks()
    {
        using var session = new TerminalSession();
        foreach (byte value in Encoding.UTF8.GetBytes("中文A"))
            session.FeedOutput([value]);
        Assert.Equal("中文A", session.GetVisibleText().Trim());
        Assert.True(session.Buffer.GetCell(0, 0).Style.Wide);
        Assert.True(session.Buffer.GetCell(0, 1).Style.Continuation);
        Assert.Equal('文', session.Buffer.GetCell(0, 2).Character.Value);
        Assert.Equal('A', session.Buffer.GetCell(0, 4).Character.Value);
    }

    /// <summary>任意数据块边界都不应截断 CSI 真彩色参数或随后的文本。</summary>
    [Fact]
    public void FeedOutput_PreservesCsiAcrossEverySplitPosition()
    {
        byte[] bytes = "\x1b[38;2;12;34;56mX\x1b[0mY"u8.ToArray();
        for (int split = 1; split < bytes.Length; split++)
        {
            using var session = new TerminalSession();
            session.FeedOutput(bytes.AsSpan(0, split));
            session.FeedOutput(bytes.AsSpan(split));
            Assert.Equal("XY", session.GetVisibleText().Trim());
            var color = session.Buffer.GetCell(0, 0).Style.Foreground;
            Assert.Equal((12, 34, 56), ((int)color.R, (int)color.G, (int)color.B));
            Assert.True(session.Buffer.GetCell(0, 1).Style.ForegroundIsDefault);
        }
    }

    /// <summary>全屏程序退出备用屏幕后恢复之前的主屏幕。</summary>
    [Fact]
    public void AlternateScreen_RestoresPrimaryScreen()
    {
        using var session = new TerminalSession();
        session.FeedOutput("shell\x1b[?1049h\x1b[2J\x1b[Heditor"u8);
        Assert.True(session.Buffer.IsAlternate);
        Assert.Contains("editor", session.GetVisibleText());
        session.FeedOutput("\x1b[?1049l"u8);
        Assert.False(session.Buffer.IsAlternate);
        Assert.Contains("shell", session.GetVisibleText());
        Assert.DoesNotContain("editor", session.GetVisibleText());
    }

    /// <summary>历史行环形缓冲区遵守会话上限。</summary>
    [Fact]
    public void Scrollback_RespectsConfiguredLimit()
    {
        using var session = new TerminalSession(20, 3, new TerminalOptions { ScrollbackMaxLines = 4 });
        for (int i = 0; i < 20; i++)
            session.FeedOutput(Encoding.UTF8.GetBytes($"line-{i}\r\n"));
        session.FeedOutput("\x1b[0m"u8);
        Assert.Equal(4, session.Buffer.ScrollbackCount);
        Assert.Contains("line-19", session.GetAllText());
        Assert.DoesNotContain("line-0", session.GetAllText());
    }

    /// <summary>OSC 支持两种十六进制颜色，且不会因索引越界丢失后续文本。</summary>
    [Theory]
    [InlineData("#abc", 170, 187, 204)]
    [InlineData("#123456", 18, 52, 86)]
    public void OscColor_ParsesHexColor(string color, byte red, byte green, byte blue)
    {
        using var session = new TerminalSession();
        session.FeedOutput(Encoding.UTF8.GetBytes($"\x1b]10;{color}\x07OK"));
        var parsed = session.Decoder.Options.DefaultForeground;
        Assert.Equal((red, green, blue), (parsed.R, parsed.G, parsed.B));
        Assert.Equal("OK", session.GetVisibleText().Trim());
    }

    /// <summary>数据块末尾的换行无需等待下一块数据才能落实到光标位置。</summary>
    [Fact]
    public void TrailingNewline_MovesCursorWithoutWaitingForNextChunk()
    {
        using var session = new TerminalSession();
        session.FeedOutput("done\r\n"u8);
        Assert.Equal((0, 1), (session.Decoder.State.CursorX, session.Decoder.State.CursorY));
    }

    /// <summary>未知控制序列不应中断随后到达的正常文字。</summary>
    [Fact]
    public void UnknownSequence_DoesNotDiscardFollowingText()
    {
        using var session = new TerminalSession();
        session.FeedOutput("\x1b[9999zOK"u8);
        Assert.Equal("OK", session.GetVisibleText().Trim());
    }
}
