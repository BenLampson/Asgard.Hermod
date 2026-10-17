// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Rendering;

/// <summary>表示按行排列、首尾包含的终端文字选区。</summary>
public readonly struct TerminalSelection
{
    /// <summary>选区起始列。</summary>
    public int StartX { get; }
    /// <summary>选区起始行。</summary>
    public int StartY { get; }
    /// <summary>选区结束列。</summary>
    public int EndX { get; }
    /// <summary>选区结束行。</summary>
    public int EndY { get; }

    /// <summary>初始化 TerminalSelection 实例。</summary>
    public TerminalSelection(int x1, int y1, int x2, int y2)
    {
        if (y1 < y2 || (y1 == y2 && x1 <= x2))
        {
            StartY = y1;
            StartX = x1;
            EndY = y2;
            EndX = x2;
        }
        else
        {
            StartY = y2;
            StartX = x2;
            EndY = y1;
            EndX = x1;
        }
    }

    /// <summary>判断单元格是否位于首尾包含的选区。</summary>
    public bool Contains(int y, int x)
    {
        if (y < StartY || y > EndY)
            return false;
        if (y == StartY && x < StartX)
            return false;
        if (y == EndY && x > EndX)
            return false;
        return true;
    }
}
