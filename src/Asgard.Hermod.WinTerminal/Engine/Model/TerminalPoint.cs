// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Model;

/// <summary>表示终端缓冲区中的列、行坐标。</summary>
public readonly struct TerminalPoint : IEquatable<TerminalPoint>
{
    /// <summary>列坐标。</summary>
    public int X { get; }
    /// <summary>行坐标。</summary>
    public int Y { get; }

    /// <summary>初始化 TerminalPoint 实例。</summary>
    public TerminalPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>比较两个值是否相等。</summary>
    public bool Equals(TerminalPoint other) => X == other.X && Y == other.Y;
    /// <summary>比较两个值是否相等。</summary>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TerminalPoint other && Equals(other);
    /// <summary>计算值的哈希代码。</summary>
    public override int GetHashCode() => HashCode.Combine(X, Y);
    /// <summary>处理 ToString 对应的终端协议或渲染逻辑。</summary>
    public override string ToString() => $"({X}, {Y})";
    /// <summary>比较或组合终端值。</summary>
    public static bool operator ==(TerminalPoint left, TerminalPoint right) => left.Equals(right);
    /// <summary>比较或组合终端值。</summary>
    public static bool operator !=(TerminalPoint left, TerminalPoint right) => !left.Equals(right);
}
