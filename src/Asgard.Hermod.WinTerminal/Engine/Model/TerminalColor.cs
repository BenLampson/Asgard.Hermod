// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Model;

/// <summary>表示终端使用的 ARGB 颜色。</summary>
public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    /// <summary>透明度。</summary>
    public byte A { get; }
    /// <summary>红色分量。</summary>
    public byte R { get; }
    /// <summary>绿色分量。</summary>
    public byte G { get; }
    /// <summary>蓝色分量。</summary>
    public byte B { get; }

    /// <summary>初始化 TerminalColor 实例。</summary>
    public TerminalColor(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>从 ARGB 分量构造颜色。</summary>
    public static TerminalColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    /// <summary>从 ARGB 分量构造颜色。</summary>
    public static TerminalColor FromArgb(int a, int r, int g, int b) => new(Clamp(a), Clamp(r), Clamp(g), Clamp(b));
    /// <summary>从 ARGB 分量构造颜色。</summary>
    public static TerminalColor FromArgb(int r, int g, int b) => new(255, Clamp(r), Clamp(g), Clamp(b));
    /// <summary>从 ARGB 分量构造颜色。</summary>
    public static TerminalColor FromArgb(int argb)
    {
        uint value = unchecked((uint)argb);
        return new((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    /// <summary>导出 32 位 ARGB 颜色。</summary>
    public uint ToArgb() => (uint)((A << 24) | (R << 16) | (G << 8) | B);
    /// <summary>黑色。</summary>
    public static TerminalColor Black { get; } = new(255, 0, 0, 0);
    /// <summary>白色。</summary>
    public static TerminalColor White { get; } = new(255, 255, 255, 255);
    /// <summary>保存或提供 Red 对应的终端状态。</summary>
    public static TerminalColor Red { get; } = new(255, 255, 0, 0);
    /// <summary>保存或提供 Green 对应的终端状态。</summary>
    public static TerminalColor Green { get; } = new(255, 0, 128, 0);
    /// <summary>保存或提供 Blue 对应的终端状态。</summary>
    public static TerminalColor Blue { get; } = new(255, 0, 0, 255);
    /// <summary>保存或提供 HotPink 对应的终端状态。</summary>
    public static TerminalColor HotPink { get; } = new(255, 255, 105, 180);
    /// <summary>保存或提供 Cyan 对应的终端状态。</summary>
    public static TerminalColor Cyan { get; } = new(255, 0, 255, 255);
    /// <summary>保存或提供 Yellow 对应的终端状态。</summary>
    public static TerminalColor Yellow { get; } = new(255, 255, 255, 0);
    /// <summary>保存或提供 Magenta 对应的终端状态。</summary>
    public static TerminalColor Magenta { get; } = new(255, 255, 0, 255);

    /// <summary>比较两个值是否相等。</summary>
    public bool Equals(TerminalColor other) => A == other.A && R == other.R && G == other.G && B == other.B;
    /// <summary>比较两个值是否相等。</summary>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TerminalColor other && Equals(other);
    /// <summary>计算值的哈希代码。</summary>
    public override int GetHashCode() => HashCode.Combine(A, R, G, B);
    /// <summary>处理 ToString 对应的终端协议或渲染逻辑。</summary>
    public override string ToString() => $"#{ToArgb():X8}";
    /// <summary>比较或组合终端值。</summary>
    public static bool operator ==(TerminalColor left, TerminalColor right) => left.Equals(right);
    /// <summary>比较或组合终端值。</summary>
    public static bool operator !=(TerminalColor left, TerminalColor right) => !left.Equals(right);
    /// <summary>处理 Clamp 对应的终端协议或渲染逻辑。</summary>
    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
