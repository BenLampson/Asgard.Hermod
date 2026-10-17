// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Model;

/// <summary>表示终端的列数与行数。</summary>
public readonly struct TerminalSize : IEquatable<TerminalSize>
{
    /// <summary>宽度。</summary>
    public int Width { get; }
    /// <summary>高度。</summary>
    public int Height { get; }

    /// <summary>初始化 TerminalSize 实例。</summary>
    public TerminalSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>比较两个值是否相等。</summary>
    public bool Equals(TerminalSize other) => Width == other.Width && Height == other.Height;
    /// <summary>比较两个值是否相等。</summary>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TerminalSize other && Equals(other);
    /// <summary>计算值的哈希代码。</summary>
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    /// <summary>处理 ToString 对应的终端协议或渲染逻辑。</summary>
    public override string ToString() => $"{Width}x{Height}";
    /// <summary>比较或组合终端值。</summary>
    public static bool operator ==(TerminalSize left, TerminalSize right) => left.Equals(right);
    /// <summary>比较或组合终端值。</summary>
    public static bool operator !=(TerminalSize left, TerminalSize right) => !left.Equals(right);
}
