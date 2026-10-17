// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine;

/// <summary>将 VT/ANSI 解析事件应用到终端缓冲区、光标、样式和工作模式。</summary>
public sealed partial class TerminalDecoder
{
    /// <summary>执行 ESC 指令。</summary>
    public void EscDispatch(byte final, ReadOnlySpan<byte> intermediates)
    {
        FlushPendingNewline();
        switch ((char)final)
        {
            case '7':
                State.SaveCursor();
                break;
            case '8':
                State.RestoreCursor();
                break;
            case 'D':
                Index();
                break;
            case 'E':
                NewLine();
                break;
            case 'H':
                Buffer.SetTabStop(State.CursorX);
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                FullReset();
                break;
            case '=':
                State.Modes.ApplicationKeypad = true;
                break;
            case '>':
                State.Modes.ApplicationKeypad = false;
                break;
            case '(':
            case ')':
            case '*':
            case '+':
                break;
            case '#':
                if (intermediates.Length == 0 || intermediates[0] == '8')
                    AlignmentTest();
                break;
            default:
                break;
        }
    }

    /// <summary>执行 CSI 指令。</summary>
    public void CsiDispatch(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker)
    {
        FlushPendingNewline();
        char fc = (char)final;
        try
        {
            if (fc == 'q' && intermediates.Length == 1 && intermediates[0] == ' ')
                return;
            if (fc == 'p' && intermediates.Length == 1 && intermediates[0] == '$' && privateMarker == '?')
            {
                RequestSelectionOrSetting(parameters.At(0, 0));
                return;
            }

            switch (fc)
            {
                case 'A':
                    CursorVertical(-parameters.At(0, 1));
                    break;
                case 'B':
                    CursorVertical(+parameters.At(0, 1));
                    break;
                case 'C':
                    CursorHorizontal(+parameters.At(0, 1));
                    break;
                case 'D':
                    CursorHorizontal(-parameters.At(0, 1));
                    break;
                case 'E':
                    CursorLine(parameters.At(0, 1), +1);
                    break;
                case 'F':
                    CursorLine(parameters.At(0, 1), -1);
                    break;
                case 'G':
                case '`':
                    CursorToColumn(parameters.At(0, 1));
                    break;
                case 'd':
                    CursorToRow(parameters.At(0, 1));
                    break;
                case 'e':
                    CursorVertical(+parameters.At(0, 1));
                    break;
                case 'H':
                case 'f':
                    CursorPositionSet(parameters.At(0, 1), parameters.At(1, 1));
                    break;
                case 'J':
                    EraseDisplay(parameters.At(0, 0));
                    break;
                case 'K':
                    EraseLine(parameters.At(0, 0));
                    break;
                case 'X':
                    Buffer.EraseChars(State.CursorY, State.CursorX, Math.Max(1, parameters.At(0, 1)));
                    break;
                case '@':
                    Buffer.InsertChars(State.CursorY, State.CursorX, Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'P':
                    Buffer.DeleteChars(State.CursorY, State.CursorX, Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'L':
                    InsertLines(Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'M':
                    DeleteLines(Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'S':
                    Buffer.ScrollUp(Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'T':
                    Buffer.ScrollDown(Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'I':
                    ForwardTab(Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'Z':
                    BackwardTab(Math.Max(1, parameters.At(0, 1)));
                    break;
                case 'g':
                    ClearTab(parameters.At(0, 0));
                    break;
                case 's':
                    State.SaveCursor();
                    break;
                case 'u':
                    State.RestoreCursor();
                    break;
                case 'h':
                    SetModes(parameters, set: true, privateMarker);
                    break;
                case 'l':
                    SetModes(parameters, set: false, privateMarker);
                    break;
                case 'n':
                    DeviceStatusReport(parameters.At(0, 0));
                    break;
                case 'c':
                case '>':
                    DeviceAttributes(privateMarker);
                    break;
                case 'x':
                    DecRequestParameters(parameters.At(0, 1));
                    break;
                case 'r':
                    SetScrollRegion(parameters.At(0, 1), parameters.At(1, Buffer.Rows));
                    break;
                case 'm':
                    SetGraphicRendition(parameters);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TerminalDecoder.CsiDispatch failed for final {fc}.\n{ex}");
        }
    }

    /// <summary>执行 OSC 指令。</summary>
    public void OscDispatch(ReadOnlySpan<byte> data)
    {
        FlushPendingNewline();
        OscDispatcher.ProcessOsc(this, data);
    }

    /// <summary>开始接收 DCS 负载。</summary>
    public void DcsHook(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker)
    {
        FlushPendingNewline();
    }

    /// <summary>接收 DCS 负载。</summary>
    public void DcsPut(byte b)
    {
    }

    /// <summary>结束 DCS 负载。</summary>
    public void DcsUnhook()
    {
        FlushPendingNewline();
    }

    /// <summary>处理 SosPmApcDispatch 对应的终端协议或渲染逻辑。</summary>
    public void SosPmApcDispatch(byte kind, ReadOnlySpan<byte> data)
    {
        FlushPendingNewline();
    }

    /// <summary>处理 CursorLeft 对应的终端协议或渲染逻辑。</summary>
    private void CursorLeft()
    {
        if (State.CursorX > 0)
            State.CursorX--;
        State.WrapPending = false;
    }

    /// <summary>处理 Tab 对应的终端协议或渲染逻辑。</summary>
    private void Tab()
    {
        State.CursorX = Buffer.NextTabStop(State.CursorX);
        State.WrapPending = false;
    }

    /// <summary>处理 CarriageReturn 对应的终端协议或渲染逻辑。</summary>
    private void CarriageReturn()
    {
        State.CursorX = 0;
        State.WrapPending = false;
    }

    /// <summary>处理 LineFeed 对应的终端协议或渲染逻辑。</summary>
    private void LineFeed()
    {
        Index();
        if (State.Modes.LineFeedNewLine)
            State.CursorX = 0;
    }

    /// <summary>处理 Index 对应的终端协议或渲染逻辑。</summary>
    private void Index()
    {
        if (State.CursorY < Buffer.ScrollBottom)
            State.CursorY++;
        else
            Buffer.ScrollUp(Buffer.ScrollTop, Buffer.ScrollBottom, 1);
        State.WrapPending = false;
    }

    /// <summary>处理 NewLine 对应的终端协议或渲染逻辑。</summary>
    private void NewLine()
    {
        State.CursorX = 0;
        Index();
    }

    /// <summary>处理 ReverseIndex 对应的终端协议或渲染逻辑。</summary>
    private void ReverseIndex()
    {
        if (State.CursorY > Buffer.ScrollTop)
            State.CursorY--;
        else
            Buffer.ScrollDown(Buffer.ScrollTop, Buffer.ScrollBottom, 1);
        State.WrapPending = false;
    }

    /// <summary>处理 CursorVertical 对应的终端协议或渲染逻辑。</summary>
    private void CursorVertical(int delta)
    {
        int limit = delta < 0 ? (State.CursorY >= Buffer.ScrollTop ? Buffer.ScrollTop : 0) : (State.CursorY <= Buffer.ScrollBottom ? Buffer.ScrollBottom : Buffer.Rows - 1);
        State.CursorY = Math.Clamp(State.CursorY + delta, 0, Buffer.Rows - 1);
        if (delta < 0 && State.CursorY < limit)
            State.CursorY = limit;
        if (delta > 0 && State.CursorY > limit)
            State.CursorY = limit;
        State.WrapPending = false;
    }

    /// <summary>处理 CursorHorizontal 对应的终端协议或渲染逻辑。</summary>
    private void CursorHorizontal(int delta)
    {
        State.CursorX = Math.Clamp(State.CursorX + delta, 0, Buffer.Columns - 1);
        State.WrapPending = false;
    }

    /// <summary>处理 CursorLine 对应的终端协议或渲染逻辑。</summary>
    private void CursorLine(int count, int dir)
    {
        count = Math.Max(1, count);
        State.CursorY = Math.Clamp(State.CursorY + dir * count, 0, Buffer.Rows - 1);
        State.CursorX = 0;
        State.WrapPending = false;
    }

    /// <summary>处理 CursorToColumn 对应的终端协议或渲染逻辑。</summary>
    private void CursorToColumn(int column)
    {
        State.CursorX = Math.Clamp(column - 1, 0, Buffer.Columns - 1);
        State.WrapPending = false;
    }

    /// <summary>处理 CursorToRow 对应的终端协议或渲染逻辑。</summary>
    private void CursorToRow(int row)
    {
        int target = State.Modes.OriginMode ? Buffer.ScrollTop + row - 1 : row - 1;
        State.CursorY = Math.Clamp(target, 0, Buffer.Rows - 1);
        State.WrapPending = false;
    }

    /// <summary>处理 CursorPositionSet 对应的终端协议或渲染逻辑。</summary>
    private void CursorPositionSet(int row, int col)
    {
        int y = State.Modes.OriginMode ? Buffer.ScrollTop + row - 1 : row - 1;
        int x = col - 1;
        State.CursorY = Math.Clamp(y, 0, Buffer.Rows - 1);
        State.CursorX = Math.Clamp(x, 0, Buffer.Columns - 1);
        State.WrapPending = false;
    }
}
