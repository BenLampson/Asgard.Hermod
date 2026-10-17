namespace Asgard.Hermod.WinTerminal;

/// <summary>搜索本机已接收的输出，选区使用字符单元格坐标，不执行远程命令。</summary>
public sealed partial class TerminalControl
{
    private string _searchQuery = "";
    private int _searchIndex = -1;

    /// <summary>按行搜索历史和当前屏幕；宽字符、代理对与匹配选区使用相同列映射。</summary>
    public (int Index, int Count) FindText(string query, bool forward = true, bool advance = true)
    {
        VerifyAccess();
        if (_session is not { IsDisposed: false } session || query.Length == 0)
        { _searchQuery = ""; _searchIndex = -1; _selection = null; InvalidateVisual(); return (0, 0); }
        var matches = new List<TerminalSelection>();
        lock (session.Buffer.SyncRoot)
        {
            var buffer = session.Buffer;
            var history = buffer.GetScrollback();
            int count = buffer.IsAlternate ? 0 : history.Count;
            for (int y = 0; y < count + buffer.Rows; y++)
            {
                Span<TerminalCellInfo> row = y < count ? history[y].AsSpan() : buffer.GetRow(y - count);
                var text = new StringBuilder(); var columns = new List<int>();
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].Style.Continuation) continue;
                    string rune = row[x].Character.ToString(); text.Append(rune);
                    for (int i = 0; i < rune.Length; i++) columns.Add(x);
                }
                string line = text.ToString().TrimEnd();
                for (int start = 0; start < line.Length;)
                {
                    int found = line.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    int last = columns[found + query.Length - 1];
                    if (last + 1 < row.Length && row[last + 1].Style.Continuation) last++;
                    matches.Add(new(columns[found], y, last, y)); start = found + query.Length;
                }
            }
            if (matches.Count == 0) { _selection = null; _searchIndex = -1; _searchQuery = query; InvalidateVisual(); return (0, 0); }
            _searchIndex = _searchQuery == query && _searchIndex >= 0
                ? advance ? (_searchIndex + (forward ? 1 : -1) + matches.Count) % matches.Count : Math.Min(_searchIndex, matches.Count - 1)
                : forward ? 0 : matches.Count - 1;
            _searchQuery = query; _selection = matches[_searchIndex];
            // 查找基于当前缓冲快照；尚未绘制的旧输出通知不能立刻撤销这个新选区。
            _renderedRevision = session.Revision;
            _scrollOffset = Math.Clamp(count - _selection.Value.StartY, 0, count);
        }
        InvalidateVisual();
        return (_searchIndex + 1, matches.Count);
    }

    /// <summary>只清空本机显示和历史，不发送清理命令或删除服务器日志。</summary>
    public void ClearDisplay()
    {
        VerifyAccess();
        if (_session is not { IsDisposed: false } session) return;
        _selection = null; _scrollOffset = 0; _searchQuery = ""; _searchIndex = -1;
        session.FeedOutput("\x1b[3J\x1b[2J\x1b[H"u8);
        InvalidateVisual();
    }
}
