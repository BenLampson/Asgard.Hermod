// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Model;

/// <summary>维护 256 色 ANSI 调色板。</summary>
public sealed class TerminalPalette
{
    /// <summary>保存或提供 CubeLevels 对应的终端状态。</summary>
    private static readonly byte[] CubeLevels = [0, 95, 135, 175, 215, 255];
    /// <summary>保存或提供 _colors 对应的终端状态。</summary>
    private readonly TerminalColor[] _colors = new TerminalColor[256];
    /// <summary>初始化 TerminalPalette 实例。</summary>
    public TerminalPalette() => LoadXtermDefaults();
    /// <summary>保存或提供 索引器 对应的终端状态。</summary>
    public TerminalColor this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 256);
            return _colors[index];
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 256);
            _colors[index] = value;
        }
    }

    /// <summary>获取单元格跨度。</summary>
    public Span<TerminalColor> AsSpan() => _colors.AsSpan();
    /// <summary>处理 GetColor 对应的终端协议或渲染逻辑。</summary>
    public TerminalColor GetColor(int index)
    {
        if (index < 0)
            return _colors[0];
        if (index > 255)
            return _colors[255];
        return _colors[index];
    }

    /// <summary>处理 SetAnsiColor 对应的终端协议或渲染逻辑。</summary>
    public void SetAnsiColor(int index, TerminalColor color) => this[index] = color;
    /// <summary>处理 LoadXtermDefaults 对应的终端协议或渲染逻辑。</summary>
    public void LoadXtermDefaults()
    {
        _colors[0] = TerminalColor.FromArgb(0x00, 0x00, 0x00);
        _colors[1] = TerminalColor.FromArgb(0xCD, 0x00, 0x00);
        _colors[2] = TerminalColor.FromArgb(0x00, 0xCD, 0x00);
        _colors[3] = TerminalColor.FromArgb(0xCD, 0xCD, 0x00);
        _colors[4] = TerminalColor.FromArgb(0x00, 0x00, 0xEE);
        _colors[5] = TerminalColor.FromArgb(0xCD, 0x00, 0xCD);
        _colors[6] = TerminalColor.FromArgb(0x00, 0xCD, 0xCD);
        _colors[7] = TerminalColor.FromArgb(0xE5, 0xE5, 0xE5);
        _colors[8] = TerminalColor.FromArgb(0x7F, 0x7F, 0x7F);
        _colors[9] = TerminalColor.FromArgb(0xFF, 0x00, 0x00);
        _colors[10] = TerminalColor.FromArgb(0x00, 0xFF, 0x00);
        _colors[11] = TerminalColor.FromArgb(0xFF, 0xFF, 0x00);
        _colors[12] = TerminalColor.FromArgb(0x5C, 0x5C, 0xFF);
        _colors[13] = TerminalColor.FromArgb(0xFF, 0x00, 0xFF);
        _colors[14] = TerminalColor.FromArgb(0x00, 0xFF, 0xFF);
        _colors[15] = TerminalColor.FromArgb(0xFF, 0xFF, 0xFF);
        for (int i = 0; i < 216; i++)
        {
            int r = CubeLevels[i / 36 % 6];
            int g = CubeLevels[i / 6 % 6];
            int b = CubeLevels[i % 6];
            _colors[16 + i] = TerminalColor.FromArgb((byte)r, (byte)g, (byte)b);
        }

        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            _colors[232 + i] = TerminalColor.FromArgb(v, v, v);
        }
    }
}
