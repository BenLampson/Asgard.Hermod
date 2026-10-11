// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Buffer;

/// <summary>保存 DEC/xterm 模式开关。</summary>
public sealed class TerminalModes
{
    /// <summary>应用程序光标键模式。</summary>
    public bool ApplicationCursorKeys;
    /// <summary>插入模式。</summary>
    public bool InsertMode;
    /// <summary>滚动区域原点模式。</summary>
    public bool OriginMode;
    /// <summary>自动换行模式。</summary>
    public bool AutoWrap = true;
    /// <summary>光标闪烁开关。</summary>
    public bool CursorBlink;
    /// <summary>光标可见状态。</summary>
    public bool CursorVisible = true;
    /// <summary>屏幕反色模式。</summary>
    public bool ReverseVideo;
    /// <summary>应用程序数字键盘模式。</summary>
    public bool ApplicationKeypad;
    /// <summary>退格发送 Ctrl+H 模式。</summary>
    public bool BackspaceSendsControlH;
    /// <summary>换行模式。</summary>
    public bool LineFeedNewLine;
    /// <summary>括号粘贴模式。</summary>
    public bool BracketedPaste;
    /// <summary>焦点报告模式。</summary>
    public bool FocusReporting;
    /// <summary>鼠标跟踪模式。</summary>
    public MouseTrackingMode MouseTracking = MouseTrackingMode.Off;
    /// <summary>SGR 鼠标编码模式。</summary>
    public bool SgrMouseEncoding;
    /// <summary>复制当前配置或缓冲行。</summary>
    public TerminalModes Clone() => new()
    {
        ApplicationCursorKeys = ApplicationCursorKeys,
        InsertMode = InsertMode,
        OriginMode = OriginMode,
        AutoWrap = AutoWrap,
        CursorBlink = CursorBlink,
        CursorVisible = CursorVisible,
        ReverseVideo = ReverseVideo,
        ApplicationKeypad = ApplicationKeypad,
        BackspaceSendsControlH = BackspaceSendsControlH,
        LineFeedNewLine = LineFeedNewLine,
        BracketedPaste = BracketedPaste,
        FocusReporting = FocusReporting,
        MouseTracking = MouseTracking,
        SgrMouseEncoding = SgrMouseEncoding,
    };
}
