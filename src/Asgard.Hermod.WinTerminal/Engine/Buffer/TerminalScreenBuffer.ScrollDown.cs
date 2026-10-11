// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Buffer;

/// <summary>维护主屏幕、备用屏幕、历史行、滚动区域、制表位与脏行标记。</summary>
public sealed partial class TerminalScreenBuffer
{
    /// <summary>向下滚动指定区域。</summary>
    public void ScrollDown(int top, int bottom, int n)
    {
        if (n <= 0 || top > bottom)
            return;
        int region = bottom - top + 1;
        if (n > region)
            n = region;
        TerminalRow[] leaving = new TerminalRow[n];
        for (int i = 0; i < n; i++)
            leaving[i] = _active[bottom - i];
        Array.Copy(_active, top, _active, top + n, region - n);
        for (int i = 0; i < n; i++)
        {
            leaving[i].Clear();
            _active[top + i] = leaving[i];
        }

        for (int i = top; i <= bottom; i++)
            _active[i].Dirty = true;
        MarkAllDirty();
    }

    /// <summary>在当前行插入空白行。</summary>
    public void InsertLines(int y, int n, int bottom) => ScrollDown(y, bottom, n);
    /// <summary>移除指定行并补充空白行。</summary>
    public void DeleteLines(int y, int n, int bottom) => ScrollUp(y, bottom, n);
    /// <summary>插入空白单元格并移动右侧内容。</summary>
    public void InsertChars(int y, int x, int n)
    {
        if (n <= 0 || y < 0 || y >= _rows)
            return;
        ref TerminalCellInfo[] cells = ref _active[y].Cells;
        int len = cells.Length;
        if (x >= len)
            return;
        if (n > len - x)
            n = len - x;
        Span<TerminalCellInfo> span = cells.AsSpan();
        span.Slice(x, len - x - n).CopyTo(span.Slice(x + n));
        span.Slice(x, n).Fill(TerminalCellInfo.Blank);
        _active[y].Dirty = true;
    }

    /// <summary>删除单元格并移动右侧内容。</summary>
    public void DeleteChars(int y, int x, int n)
    {
        if (n <= 0 || y < 0 || y >= _rows)
            return;
        ref TerminalCellInfo[] cells = ref _active[y].Cells;
        int len = cells.Length;
        if (x >= len)
            return;
        if (n > len - x)
            n = len - x;
        Span<TerminalCellInfo> span = cells.AsSpan();
        span.Slice(x + n, len - x - n).CopyTo(span.Slice(x));
        span.Slice(len - n, n).Fill(TerminalCellInfo.Blank);
        _active[y].Dirty = true;
    }

    /// <summary>擦除单元格而不移动其余内容。</summary>
    public void EraseChars(int y, int x, int n) => _active[y].ClearRange(x, n);
    /// <summary>切换到备用屏幕，保留主屏幕。</summary>
    public void EnterAlternateScreen()
    {
        if (_isAlt)
            return;
        _alt ??= new TerminalRow[_rows];
        if (_alt.Length != _rows)
            Array.Resize(ref _alt, _rows);
        for (int i = 0; i < _rows; i++)
        {
            _alt[i] ??= new TerminalRow(_columns);
            if (_alt[i].Length != _columns)
                _alt[i].Resize(_columns);
            _alt[i].Clear();
        }

        _active = _alt;
        _isAlt = true;
        ResetScrollRegion();
    }

    /// <summary>恢复主屏幕。</summary>
    public void LeaveAlternateScreen()
    {
        if (!_isAlt)
            return;
        _active = _primary;
        _isAlt = false;
        ResetScrollRegion();
        MarkAllDirty();
    }

    /// <summary>清空备用屏幕。</summary>
    public void ClearAlternateScreen()
    {
        if (!_isAlt)
            return;
        for (int i = 0; i < _rows; i++)
            _active[i].Clear();
    }

    /// <summary>调整缓冲区尺寸并约束光标位置。</summary>
    public void Resize(int columns, int rows) => Resize(columns, rows, state: null, pushScrollback: true);
    /// <summary>调整缓冲区尺寸并约束光标位置。</summary>
    public void Resize(int columns, int rows, TerminalState? state) => Resize(columns, rows, state, pushScrollback: true);
    /// <summary>调整缓冲区尺寸并约束光标位置。</summary>
    public void Resize(int columns, int rows, TerminalState? state, bool pushScrollback)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        int oldRows = _rows;
        int oldColumns = _columns;
        if (columns != _columns)
            Array.Resize(ref _tabStops, columns);
        ResizeGrid(ref _primary, columns, rows, pushScrollback && !_isAlt);
        if (_alt is not null)
            ResizeGrid(ref _alt, columns, rows, pushScrollback: false);
        _columns = columns;
        _rows = rows;
        _active = _isAlt ? _alt! : _primary;
        if (state is not null)
        {
            if (rows < oldRows)
            {
                int lost = oldRows - rows;
                state.CursorY = Math.Max(0, state.CursorY - lost);
            }

            state.CursorX = Math.Min(state.CursorX, _columns - 1);
            state.CursorY = Math.Clamp(state.CursorY, 0, _rows - 1);
            state.WrapPending = false;
        }

        ResetScrollRegion();
        InitDefaultTabStops();
        MarkAllDirty();
    }

    /// <summary>处理 ResizeGrid 对应的终端协议或渲染逻辑。</summary>
    private void ResizeGrid(ref TerminalRow[] grid, int columns, int rows, bool pushScrollback)
    {
        for (int i = 0; i < grid.Length; i++)
            grid[i].Resize(columns);
        if (rows < grid.Length)
        {
            int lost = grid.Length - rows;
            if (pushScrollback && !_isAlt && _scrollbackMax > 0)
            {
                for (int i = 0; i < lost; i++)
                {
                    _scrollback.Enqueue(grid[i]);
                    while (_scrollback.Count > _scrollbackMax)
                        _scrollback.Dequeue();
                }
            }

            if (lost > 0)
                Array.Copy(grid, lost, grid, 0, rows);
            Array.Resize(ref grid, rows);
        }
        else if (rows > grid.Length)
        {
            int old = grid.Length;
            Array.Resize(ref grid, rows);
            for (int i = old; i < rows; i++)
                grid[i] = new TerminalRow(columns);
        }

        InvalidateScrollbackSnapshot();
    }

    /// <summary>获取历史行快照。</summary>
    public IReadOnlyList<TerminalRow> GetScrollback() => Array.AsReadOnly(_scrollbackSnapshot ??= _scrollback.ToArray());
    /// <summary>处理 InvalidateScrollbackSnapshot 对应的终端协议或渲染逻辑。</summary>
    private void InvalidateScrollbackSnapshot() => _scrollbackSnapshot = null;
    /// <summary>导出当前屏幕的纯文本。</summary>
    public string GetVisibleText()
    {
        StringBuilder sb = new(_rows * (_columns + 1));
        for (int y = 0; y < _rows; y++)
        {
            AppendRow(sb, _active[y]);
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>导出历史和当前屏幕的纯文本。</summary>
    public string GetAllText()
    {
        if (_isAlt)
            return GetVisibleText();
        StringBuilder sb = new((_scrollback.Count + _rows) * (_columns + 1));
        foreach (TerminalRow row in _scrollback)
        {
            AppendRow(sb, row);
            sb.Append('\n');
        }

        for (int y = 0; y < _rows; y++)
        {
            AppendRow(sb, _active[y]);
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>处理 AppendRow 对应的终端协议或渲染逻辑。</summary>
    private static void AppendRow(StringBuilder sb, TerminalRow row)
    {
        int last = row.Length - 1;
        while (last >= 0)
        {
            TerminalCellInfo cell = row.Cells[last];
            if (cell.Style.Continuation)
            {
                last--;
                continue;
            }

            if (cell.Character.Value != ' ')
                break;
            last--;
        }

        for (int i = 0; i <= last; i++)
        {
            if (row.Cells[i].Style.Continuation)
                continue;
            sb.Append(row.Cells[i].Character.ToString());
        }
    }

    /// <summary>释放缓冲区、解析器或字体缓存资源。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _scrollback.Clear();
        InvalidateScrollbackSnapshot();
        _disposed = true;
    }
}
