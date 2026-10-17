// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine;

/// <summary>将 VT/ANSI 解析事件应用到终端缓冲区、光标、样式和工作模式。</summary>
public sealed partial class TerminalDecoder
{
    /// <summary>处理 EraseDisplay 对应的终端协议或渲染逻辑。</summary>
    private void EraseDisplay(int mode)
    {
        int cx = State.CursorX, cy = State.CursorY;
        switch (mode)
        {
            case 0:
                Buffer.ClearCells(cy, cx, Buffer.Columns - cx);
                Buffer.ClearRows(cy + 1, Buffer.Rows - cy - 1);
                break;
            case 1:
                Buffer.ClearRows(0, cy);
                Buffer.ClearCells(cy, 0, cx + 1);
                break;
            case 2:
                Buffer.ClearRows(0, Buffer.Rows);
                break;
            case 3:
                Buffer.ClearRows(0, Buffer.Rows);
                break;
        }
    }

    /// <summary>处理 EraseLine 对应的终端协议或渲染逻辑。</summary>
    private void EraseLine(int mode)
    {
        int cx = State.CursorX, cy = State.CursorY;
        switch (mode)
        {
            case 0:
                Buffer.ClearCells(cy, cx, Buffer.Columns - cx);
                break;
            case 1:
                Buffer.ClearCells(cy, 0, cx + 1);
                break;
            case 2:
                Buffer.ClearCells(cy, 0, Buffer.Columns);
                break;
        }
    }

    /// <summary>在当前行插入空白行。</summary>
    private void InsertLines(int n)
    {
        if (State.CursorY > Buffer.ScrollBottom)
            return;
        int bottom = Buffer.ScrollBottom;
        Buffer.InsertLines(State.CursorY, n, bottom);
        State.CursorX = 0;
        State.WrapPending = false;
    }

    /// <summary>移除指定行并补充空白行。</summary>
    private void DeleteLines(int n)
    {
        if (State.CursorY > Buffer.ScrollBottom)
            return;
        int bottom = Buffer.ScrollBottom;
        Buffer.DeleteLines(State.CursorY, n, bottom);
        State.CursorX = 0;
        State.WrapPending = false;
    }

    /// <summary>处理 ForwardTab 对应的终端协议或渲染逻辑。</summary>
    private void ForwardTab(int count)
    {
        for (int i = 0; i < count; i++)
            State.CursorX = Buffer.NextTabStop(State.CursorX);
        State.WrapPending = false;
    }

    /// <summary>处理 BackwardTab 对应的终端协议或渲染逻辑。</summary>
    private void BackwardTab(int count)
    {
        for (int i = 0; i < count; i++)
            State.CursorX = Buffer.PrevTabStop(State.CursorX);
        State.WrapPending = false;
    }

    /// <summary>处理 ClearTab 对应的终端协议或渲染逻辑。</summary>
    private void ClearTab(int mode)
    {
        if (mode == 0)
            Buffer.ClearTabStop(State.CursorX);
        else if (mode == 3)
            Buffer.ClearAllTabStops();
    }

    /// <summary>处理 SetModes 对应的终端协议或渲染逻辑。</summary>
    private void SetModes(in CsiParams parameters, bool set, char marker)
    {
        for (int i = 0; i < parameters.Length; i++)
            SetMode(parameters[i], set, marker);
    }

    /// <summary>处理 SetMode 对应的终端协议或渲染逻辑。</summary>
    private void SetMode(int mode, bool set, char marker)
    {
        if (marker == '?')
        {
            switch (mode)
            {
                case 1:
                    State.Modes.ApplicationCursorKeys = set;
                    break;
                case 5:
                    State.Modes.ReverseVideo = set;
                    break;
                case 6:
                    State.Modes.OriginMode = set;
                    break;
                case 7:
                    State.Modes.AutoWrap = set;
                    break;
                case 12:
                    State.Modes.CursorBlink = set;
                    break;
                case 25:
                    State.Modes.CursorVisible = set;
                    break;
                case 47:
                case 1047:
                    {
                        if (set)
                            Buffer.EnterAlternateScreen();
                        else
                            Buffer.LeaveAlternateScreen();
                        break;
                    }

                case 1048:
                    {
                        if (set)
                            State.SaveCursor();
                        else
                            State.RestoreCursor();
                        break;
                    }

                case 1049:
                    {
                        if (set)
                        {
                            State.SaveCursor();
                            Buffer.EnterAlternateScreen();
                        }
                        else
                        {
                            Buffer.LeaveAlternateScreen();
                            State.RestoreCursor();
                        }

                        break;
                    }

                case 66:
                    State.Modes.ApplicationKeypad = set;
                    break;
                case 67:
                    State.Modes.BackspaceSendsControlH = set;
                    break;
                case 1000:
                case 1002:
                case 1003:
                    {
                        State.Modes.MouseTracking = set ? (mode == 1003 ? MouseTrackingMode.AnyEvent : mode == 1002 ? MouseTrackingMode.ButtonEvent : MouseTrackingMode.X10) : MouseTrackingMode.Off;
                        break;
                    }

                case 1006:
                    State.Modes.SgrMouseEncoding = set;
                    break;
                case 1004:
                    State.Modes.FocusReporting = set;
                    break;
                case 2004:
                    State.Modes.BracketedPaste = set;
                    break;
                default:
                    break;
            }
        }
        else
        {
            switch (mode)
            {
                case 4:
                    State.Modes.InsertMode = set;
                    break;
                case 20:
                    State.Modes.LineFeedNewLine = set;
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>设置上下边界都包含的滚动区域。</summary>
    private void SetScrollRegion(int top, int bottom)
    {
        if (top == 1 && bottom == Buffer.Rows)
            Buffer.ResetScrollRegion();
        else
            Buffer.SetScrollRegion(top - 1, bottom - 1);
        State.CursorY = State.Modes.OriginMode ? Buffer.ScrollTop : 0;
        State.CursorX = 0;
        State.WrapPending = false;
    }

    /// <summary>处理 DeviceStatusReport 对应的终端协议或渲染逻辑。</summary>
    private void DeviceStatusReport(int mode)
    {
        switch (mode)
        {
            case 5:
                Respond("\x1b[0n");
                break;
            case 6:
                int row = State.Modes.OriginMode ? State.CursorY - Buffer.ScrollTop + 1 : State.CursorY + 1;
                int col = State.CursorX + 1;
                Respond($"\x1b[{row};{col}R");
                break;
        }
    }

    /// <summary>处理 DeviceAttributes 对应的终端协议或渲染逻辑。</summary>
    private void DeviceAttributes(char marker)
    {
        if (marker == '>')
            Respond("\x1b[>41;0;0c");
        else
            Respond("\x1b[?62;0c");
    }

    /// <summary>处理 DecRequestParameters 对应的终端协议或渲染逻辑。</summary>
    private void DecRequestParameters(int ps)
    {
        Respond($"\x1b[{(ps == 1 ? 2 : 3)};1;1;112;112;1;0x");
    }

    /// <summary>处理 RequestSelectionOrSetting 对应的终端协议或渲染逻辑。</summary>
    private void RequestSelectionOrSetting(int ps)
    {
        switch (ps)
        {
            case 1:
                Respond($"\x1bP1$r{EncodeSgr()}\x1b\\");
                break;
            default:
                Respond("\x1bP0$r\x1b\\");
                break;
        }
    }

    /// <summary>处理 FullReset 对应的终端协议或渲染逻辑。</summary>
    private void FullReset()
    {
        _pendingReturn = false;
        _pendingLineFeeds = 0;
        Buffer.ClearAll();
        Buffer.ResetScrollRegion();
        State.Attributes = CharAttributes.Default;
        State.Modes = new TerminalModes();
        State.CursorX = 0;
        State.CursorY = 0;
        State.WrapPending = false;
        State.SavedAttributes = CharAttributes.Default;
        State.SavedCursorX = 0;
        State.SavedCursorY = 0;
        Buffer.ClearAllTabStops();
    }

    /// <summary>处理 AlignmentTest 对应的终端协议或渲染逻辑。</summary>
    private void AlignmentTest()
    {
        CellRenderStyle style = CharAttributes.Default.ToCellStyle(Options.Palette, Options.BoldAsBright);
        TerminalCellInfo cell = new TerminalCellInfo(new Rune('E'), style);
        for (int y = 0; y < Buffer.Rows; y++)
        {
            for (int x = 0; x < Buffer.Columns; x++)
                Buffer.GetCell(y, x) = cell;
            Buffer.MarkRowDirty(y);
        }
    }
}
