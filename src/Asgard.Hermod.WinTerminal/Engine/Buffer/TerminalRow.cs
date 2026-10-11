// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Buffer;

/// <summary>保存一行终端单元格与重绘状态。</summary>
public sealed class TerminalRow
{
    /// <summary>单元格数组。</summary>
    public TerminalCellInfo[] Cells;
    /// <summary>保存或提供 Wrapped 对应的终端状态。</summary>
    public bool Wrapped;
    /// <summary>脏行标记。</summary>
    public bool Dirty = true;
    /// <summary>初始化 TerminalRow 实例。</summary>
    public TerminalRow(int columns)
    {
        Cells = new TerminalCellInfo[columns];
        Clear();
    }

    /// <summary>保存或提供 Length 对应的终端状态。</summary>
    public int Length => Cells.Length;

    /// <summary>恢复空白内容或默认状态。</summary>
    public void Clear()
    {
        Array.Fill(Cells, TerminalCellInfo.Blank);
        Wrapped = false;
        Dirty = true;
    }

    /// <summary>清空指定单元格范围。</summary>
    public void ClearRange(int start, int count)
    {
        int end = Math.Min(start + count, Cells.Length);
        int actual = end - start;
        if (actual <= 0)
            return;
        Cells.AsSpan(start, actual).Fill(TerminalCellInfo.Blank);
        Dirty = true;
    }

    /// <summary>获取单元格跨度。</summary>
    public Span<TerminalCellInfo> AsSpan() => Cells;
    /// <summary>保存或提供 索引器 对应的终端状态。</summary>
    public ref TerminalCellInfo this[int x] => ref Cells[x];
    /// <summary>调整缓冲区尺寸并约束光标位置。</summary>
    public void Resize(int newColumns)
    {
        if (newColumns == Cells.Length)
            return;
        int oldLength = Cells.Length;
        Array.Resize(ref Cells, newColumns);
        if (newColumns > oldLength)
            Cells.AsSpan(oldLength, newColumns - oldLength).Fill(TerminalCellInfo.Blank);
        Dirty = true;
    }
}
