// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Buffer;

/// <summary>维护主屏幕、备用屏幕、历史行、滚动区域、制表位与脏行标记。</summary>
public sealed partial class TerminalScreenBuffer : IDisposable
{
    /// <summary>保存或提供 DefaultScrollbackMax 对应的终端状态。</summary>
    private const int DefaultScrollbackMax = 10000;
    /// <summary>缓冲区同步锁。</summary>
    public Lock SyncRoot { get; } = new Lock();

    /// <summary>主屏幕或主字体。</summary>
    private TerminalRow[] _primary;
    /// <summary>备用屏幕。</summary>
    private TerminalRow[]? _alt;
    /// <summary>当前屏幕。</summary>
    private TerminalRow[] _active;
    /// <summary>备用屏幕是否激活。</summary>
    private bool _isAlt;
    /// <summary>网格列数。</summary>
    private int _columns;
    /// <summary>网格行数。</summary>
    private int _rows;
    /// <summary>滚动区域顶部。</summary>
    private int _scrollTop;
    /// <summary>滚动区域底部。</summary>
    private int _scrollBottom;
    /// <summary>历史行队列。</summary>
    private readonly Queue<TerminalRow> _scrollback = new();
    /// <summary>历史行上限。</summary>
    private int _scrollbackMax = DefaultScrollbackMax;
    /// <summary>历史行快照。</summary>
    private TerminalRow[]? _scrollbackSnapshot;
    /// <summary>制表位标记。</summary>
    private bool[] _tabStops;
    /// <summary>资源是否已释放。</summary>
    private bool _disposed;
    /// <summary>列数。</summary>
    public int Columns => _columns;
    /// <summary>行数。</summary>
    public int Rows => _rows;
    /// <summary>滚动区域顶部。</summary>
    public int ScrollTop => _scrollTop;
    /// <summary>滚动区域底部。</summary>
    public int ScrollBottom => _scrollBottom;
    /// <summary>备用屏幕状态。</summary>
    public bool IsAlternate => _isAlt;
    /// <summary>历史行数量。</summary>
    public int ScrollbackCount => _scrollback.Count;
    /// <summary>历史行上限。</summary>
    public int ScrollbackMax => _scrollbackMax;
    /// <summary>终端网格尺寸。</summary>
    public TerminalSize GridSize => new TerminalSize(_columns, _rows);

    /// <summary>初始化 TerminalScreenBuffer 实例。</summary>
    public TerminalScreenBuffer(int columns, int rows, int scrollbackMax = DefaultScrollbackMax)
    {
        _columns = Math.Max(1, columns);
        _rows = Math.Max(1, rows);
        _scrollbackMax = Math.Max(0, scrollbackMax);
        _primary = new TerminalRow[_rows];
        for (int i = 0; i < _rows; i++)
            _primary[i] = new TerminalRow(_columns);
        _active = _primary;
        _tabStops = new bool[_columns];
        ResetScrollRegion();
        InitDefaultTabStops();
    }

    /// <summary>设置历史行上限并裁剪多余内容。</summary>
    public void SetScrollbackMax(int max)
    {
        _scrollbackMax = Math.Max(0, max);
        while (_scrollback.Count > _scrollbackMax)
            _scrollback.Dequeue();
        InvalidateScrollbackSnapshot();
    }

    /// <summary>获取指定行的单元格跨度；调用者持有缓冲区锁。</summary>
    public Span<TerminalCellInfo> GetRow(int y) => _active[y].AsSpan();
    /// <summary>获取指定缓冲行对象。</summary>
    public TerminalRow GetRowObject(int y) => _active[y];
    /// <summary>获取指定单元格的引用。</summary>
    public ref TerminalCellInfo GetCell(int y, int x) => ref _active[y].Cells[x];
    /// <summary>是否存在待绘制行。</summary>
    public bool HasDirtyRows
    {
        get
        {
            for (int i = 0; i < _rows; i++)
            {
                if (_active[i].Dirty)
                    return true;
            }

            return false;
        }
    }

    /// <summary>判断指定行是否需要重绘。</summary>
    public bool IsRowDirty(int y) => y >= 0 && y < _rows && _active[y].Dirty;
    /// <summary>标记指定行为待绘制。</summary>
    public void MarkRowDirty(int y)
    {
        if (y >= 0 && y < _rows)
            _active[y].Dirty = true;
    }

    /// <summary>标记指定范围为待绘制。</summary>
    public void MarkRowsDirty(int from, int count)
    {
        int end = Math.Min(from + count, _rows);
        for (int i = Math.Max(0, from); i < end; i++)
            _active[i].Dirty = true;
    }

    /// <summary>清除指定行的脏行标记。</summary>
    public void MarkRowClean(int y)
    {
        if (y >= 0 && y < _rows)
            _active[y].Dirty = false;
    }

    /// <summary>标记整个屏幕为待绘制。</summary>
    public void MarkAllDirty()
    {
        for (int i = 0; i < _rows; i++)
            _active[i].Dirty = true;
    }

    /// <summary>清空当前屏幕内容。</summary>
    public void ClearAll()
    {
        for (int i = 0; i < _rows; i++)
            _active[i].Clear();
    }

    /// <summary>清空指定范围的行。</summary>
    public void ClearRows(int from, int count)
    {
        int end = Math.Min(from + count, _rows);
        for (int i = Math.Max(0, from); i < end; i++)
            _active[i].Clear();
    }

    /// <summary>清空指定范围的单元格。</summary>
    public void ClearCells(int y, int x, int count) => _active[y].ClearRange(x, count);
    /// <summary>判断指定列是否为制表位。</summary>
    public bool IsTabStop(int x) => x >= 0 && x < _tabStops.Length && _tabStops[x];
    /// <summary>设置制表位。</summary>
    public void SetTabStop(int x)
    {
        if (x >= 0 && x < _tabStops.Length)
            _tabStops[x] = true;
    }

    /// <summary>移除制表位。</summary>
    public void ClearTabStop(int x)
    {
        if (x >= 0 && x < _tabStops.Length)
            _tabStops[x] = false;
    }

    /// <summary>移除全部制表位。</summary>
    public void ClearAllTabStops() => Array.Fill(_tabStops, false);
    /// <summary>处理 InitDefaultTabStops 对应的终端协议或渲染逻辑。</summary>
    private void InitDefaultTabStops()
    {
        Array.Fill(_tabStops, false);
        for (int x = 8; x < _tabStops.Length; x += 8)
            _tabStops[x] = true;
    }

    /// <summary>查找下一个制表位。</summary>
    public int NextTabStop(int x)
    {
        for (int i = x + 1; i < _tabStops.Length; i++)
        {
            if (_tabStops[i])
                return i;
        }

        return _tabStops.Length - 1;
    }

    /// <summary>查找上一个制表位。</summary>
    public int PrevTabStop(int x)
    {
        for (int i = x - 1; i >= 0; i--)
        {
            if (_tabStops[i])
                return i;
        }

        return 0;
    }

    /// <summary>设置上下边界都包含的滚动区域。</summary>
    public void SetScrollRegion(int top, int bottom)
    {
        if (top < 0)
            top = 0;
        if (bottom >= _rows)
            bottom = _rows - 1;
        if (top >= bottom)
            return;
        _scrollTop = top;
        _scrollBottom = bottom;
    }

    /// <summary>恢复整个屏幕为滚动区域。</summary>
    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    /// <summary>向上滚动行；主屏幕全屏滚动时保留历史。</summary>
    public void ScrollUp(int n) => ScrollUp(_scrollTop, _scrollBottom, n);
    /// <summary>向上滚动行；主屏幕全屏滚动时保留历史。</summary>
    public void ScrollUp(int top, int bottom, int n)
    {
        if (n <= 0 || top > bottom)
            return;
        int region = bottom - top + 1;
        if (n > region)
            n = region;
        TerminalRow[] leaving = new TerminalRow[n];
        for (int i = 0; i < n; i++)
            leaving[i] = _active[top + i];
        Array.Copy(_active, top + n, _active, top, region - n);
        for (int i = 0; i < n; i++)
        {
            if (!_isAlt && top == 0 && _scrollbackMax > 0)
            {
                _scrollback.Enqueue(leaving[i]);
                if (_scrollback.Count > _scrollbackMax)
                {
                    TerminalRow recycled = _scrollback.Dequeue();
                    recycled.Clear();
                    _active[bottom - n + 1 + i] = recycled;
                }
                else
                {
                    _active[bottom - n + 1 + i] = new TerminalRow(_columns);
                }
            }
            else
            {
                leaving[i].Clear();
                _active[bottom - n + 1 + i] = leaving[i];
            }
        }

        InvalidateScrollbackSnapshot();
        MarkAllDirty();
    }

    /// <summary>向下滚动指定区域。</summary>
    public void ScrollDown(int n) => ScrollDown(_scrollTop, _scrollBottom, n);
}
