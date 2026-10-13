namespace Asgard.Hermod.WinTerminal;

/// <summary>负责鼠标选择、历史滚动与 xterm 鼠标转发。</summary>
public sealed partial class TerminalControl
{
    /// <summary>选择历史与当前屏幕的全部内容。</summary>
    public void SelectAll()
    {
        VerifyAccess();
        if (_session is not { IsDisposed: false } session)
            return;
        lock (session.Buffer.SyncRoot)
        {
            var buffer = session.Buffer;
            int history = buffer.IsAlternate ? 0 : buffer.ScrollbackCount;
            _selection = new TerminalSelection(0, 0, buffer.Columns - 1, history + buffer.Rows - 1);
        }
        InvalidateVisual();
    }

    /// <summary>导出当前选区；双列字符的后续单元格不重复输出。</summary>
    public string GetSelectedText()
    {
        VerifyAccess();
        if (_selection is not { } selection || _session is not { IsDisposed: false } session)
            return string.Empty;
        var text = new StringBuilder();
        lock (session.Buffer.SyncRoot)
        {
            var buffer = session.Buffer;
            var history = buffer.GetScrollback();
            int historyCount = buffer.IsAlternate ? 0 : history.Count;
            int last = Math.Min(selection.EndY, historyCount + buffer.Rows - 1);
            for (int y = Math.Max(0, selection.StartY); y <= last; y++)
            {
                Span<TerminalCellInfo> row = y < historyCount ? history[y].AsSpan() : buffer.GetRow(y - historyCount);
                int start = y == selection.StartY ? selection.StartX : 0;
                int end = Math.Min(y == selection.EndY ? selection.EndX : row.Length - 1, row.Length - 1);
                var line = new StringBuilder();
                for (int x = Math.Max(0, start); x <= end; x++)
                    if (!row[x].Style.Continuation)
                        line.Append(row[x].Character.ToString());
                text.Append(line.ToString().TrimEnd());
                if (y != last) text.AppendLine();
            }
        }
        return text.ToString();
    }

    /// <summary>左键开始选区；应用鼠标模式下发送按钮报告，Shift 保留本地选择。</summary>
    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        base.OnMouseDown(args);
        Focus();
        if (_session is not { IsDisposed: false } session || !_fontConfigured)
            return;
        var position = GetCellPosition(args.GetPosition(this), false);
        var button = MapButton(args.ChangedButton);
        if (ShouldReportMouse())
        {
            _pressedButton = button;
            SendMouseButton(button, true, position);
            CaptureMouse();
        }
        else if (args.ChangedButton == MouseButton.Left)
        {
            _selectionAnchor = GetCellPosition(args.GetPosition(this), true);
            var anchor = _selectionAnchor.Value;
            _selection = new TerminalSelection(anchor.X, anchor.Y, anchor.X, anchor.Y);
            CaptureMouse();
        }
        else if (args.ChangedButton == MouseButton.Right && ContextMenu is null)
        {
            if (_selection is not null) CopySelection();
            else Paste();
        }
        else return;
        args.Handled = true;
        InvalidateVisual();
    }

    /// <summary>拖动更新选区，或按应用请求报告鼠标移动。</summary>
    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (_session is not { IsDisposed: false } session || !_fontConfigured)
            return;
        if (ShouldReportMouse())
        {
            lock (session.Buffer.SyncRoot)
            {
                var mode = session.Decoder.State.Modes;
                if (mode.MouseTracking == MouseTrackingMode.AnyEvent
                    || (mode.MouseTracking == MouseTrackingMode.ButtonEvent && _pressedButton is not null))
                {
                    var (x, y) = GetCellPosition(args.GetPosition(this), false);
                    SendMouseSequence(MouseEncoder.EncodeMotion(_pressedButton, x, y, GetModifiers(), mode.SgrMouseEncoding), mode.SgrMouseEncoding);
                }
            }
        }
        else if (_selectionAnchor is { } anchor && args.LeftButton == MouseButtonState.Pressed)
        {
            var (x, y) = GetCellPosition(args.GetPosition(this), true);
            _selection = new TerminalSelection(anchor.X, anchor.Y, x, y);
            InvalidateVisual();
        }
    }

    /// <summary>结束选择或发送鼠标释放报告。</summary>
    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        base.OnMouseUp(args);
        if (_pressedButton is { } button && ShouldReportMouse())
            SendMouseButton(button, false, GetCellPosition(args.GetPosition(this), false));
        _pressedButton = null;
        _selectionAnchor = null;
        ReleaseMouseCapture();
        args.Handled = true;
    }

    /// <summary>滚动历史；Ctrl 调整字号，应用鼠标模式下发送滚轮报告。</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs args)
    {
        base.OnMouseWheel(args);
        if (_session is not { IsDisposed: false } session || !_fontConfigured)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            FontSize = Math.Clamp(FontSize + Math.Sign(args.Delta), 8, 40);
        else if (ShouldReportMouse())
        {
            lock (session.Buffer.SyncRoot)
            {
                var (x, y) = GetCellPosition(args.GetPosition(this), false);
                {
                    bool sgr = session.Decoder.State.Modes.SgrMouseEncoding;
                    SendMouseSequence(MouseEncoder.EncodeWheel(args.Delta > 0, x, y, GetModifiers(), sgr), sgr);
                }
            }
        }
        else
        {
            lock (session.Buffer.SyncRoot)
                _scrollOffset = Math.Clamp(_scrollOffset + Math.Sign(args.Delta) * 3, 0, session.Buffer.IsAlternate ? 0 : session.Buffer.ScrollbackCount);
            InvalidateVisual();
        }
        args.Handled = true;
    }

    /// <summary>判断应用是否接管鼠标；Shift 强制使用本地交互。</summary>
    private bool ShouldReportMouse()
    {
        if (IsReadOnly || _session is not { IsDisposed: false } session || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return false;
        lock (session.Buffer.SyncRoot)
            return session.Decoder.State.Modes.MouseTracking != MouseTrackingMode.Off;
    }

    /// <summary>将鼠标像素坐标转换为网格或历史坐标。</summary>
    private (int X, int Y) GetCellPosition(Point point, bool includeHistory)
    {
        if (_session is not { IsDisposed: false } session)
            return (0, 0);
        lock (session.Buffer.SyncRoot)
        {
            var buffer = session.Buffer;
            int x = Math.Clamp((int)(point.X / _renderer.CellWidth), 0, buffer.Columns - 1);
            int y = Math.Clamp((int)(point.Y / _renderer.CellHeight), 0, buffer.Rows - 1);
            if (includeHistory && !buffer.IsAlternate)
                y += Math.Max(0, buffer.ScrollbackCount - _scrollOffset);
            return (x, y);
        }
    }

    /// <summary>发送一个编码后的鼠标按钮事件。</summary>
    private void SendMouseButton(TerminalMouseButton button, bool pressed, (int X, int Y) position)
    {
        if (_session is not { IsDisposed: false } session)
            return;
        lock (session.Buffer.SyncRoot)
        {
            bool sgr = session.Decoder.State.Modes.SgrMouseEncoding;
            SendMouseSequence(MouseEncoder.EncodeButton(button, pressed, position.X, position.Y, GetModifiers(), sgr), sgr);
        }
    }

    /// <summary>传统鼠标坐标使用原始单字节；SGR 报告使用 ASCII 形式。</summary>
    private void SendMouseSequence(string sequence, bool sgr)
        => _session?.SendInput((sgr ? Encoding.UTF8 : Encoding.Latin1).GetBytes(sequence));

    /// <summary>转换 WPF 鼠标按钮。</summary>
    private static TerminalMouseButton MapButton(MouseButton button) => button switch
    {
        MouseButton.Right => TerminalMouseButton.Right,
        MouseButton.Middle => TerminalMouseButton.Middle,
        MouseButton.XButton1 => TerminalMouseButton.XButton1,
        MouseButton.XButton2 => TerminalMouseButton.XButton2,
        _ => TerminalMouseButton.Left,
    };
}
