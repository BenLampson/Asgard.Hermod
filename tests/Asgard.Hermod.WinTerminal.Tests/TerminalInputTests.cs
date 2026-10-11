namespace Asgard.Hermod.WinTerminal.Tests;

/// <summary>验证特殊输入序列的协议兼容行为。</summary>
public sealed class TerminalInputTests
{
    /// <summary>方向键编码尊重应用程序光标键模式与修饰键。</summary>
    [Theory]
    [InlineData(false, TerminalModifier.None, "\x1b[A")]
    [InlineData(true, TerminalModifier.None, "\x1bOA")]
    [InlineData(true, TerminalModifier.Control, "\x1b[1;5A")]
    public void Arrow_RespectsApplicationMode(bool applicationMode, TerminalModifier modifiers, string expected)
    {
        var modes = new TerminalModes { ApplicationCursorKeys = applicationMode };
        Assert.Equal(expected, KeyboardEncoder.Encode(TerminalKey.Up, modifiers, modes));
    }

    /// <summary>默认 Enter 只发送 CR，避免 Shell 把 CR/LF 当作两次提交。</summary>
    [Fact]
    public void Enter_DefaultsToSingleCarriageReturn()
        => Assert.Equal("\r", KeyboardEncoder.Encode(TerminalKey.Enter, TerminalModifier.None, new TerminalModes()));

    /// <summary>粘贴只在远端开启括号模式时添加界定符。</summary>
    [Fact]
    public void Paste_WrapsOnlyWhenRequested()
    {
        Assert.Equal("\x1b[200~a\nb\x1b[201~", KeyboardEncoder.WrapBracketedPaste("a\nb", true));
        Assert.Equal("a\nb", KeyboardEncoder.WrapBracketedPaste("a\nb", false));
    }

    /// <summary>传统鼠标报告使用释放码 3，并支持 223 列的单字节坐标。</summary>
    [Fact]
    public void LegacyMouse_EncodesReleaseAndFullCoordinateRange()
    {
        string report = MouseEncoder.EncodeButton(TerminalMouseButton.Left, false, 222, 1, TerminalModifier.None, false);
        Assert.Equal('\x23', report[3]);
        Assert.Equal('\xff', report[4]);
        Assert.Equal('\x22', report[5]);
    }
}
