namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>Win32 COORD 使用两个有符号 16 位字段。</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeCoordinate(TerminalSize size)
{
    /// <summary>列数。</summary>
    public readonly short X = checked((short)size.Width);

    /// <summary>行数。</summary>
    public readonly short Y = checked((short)size.Height);
}
