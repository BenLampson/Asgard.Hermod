namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>与 Win32 STARTUPINFOW 保持布局一致的进程启动信息。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeStartupInformation
{
    /// <summary>扩展启动信息结构的字节数。</summary>
    public int Size;
    /// <summary>系统保留字段。</summary>
    public nint Reserved;
    /// <summary>可选桌面名称。</summary>
    public nint Desktop;
    /// <summary>可选窗口标题。</summary>
    public nint Title;
    /// <summary>窗口水平位置。</summary>
    public uint X;
    /// <summary>窗口垂直位置。</summary>
    public uint Y;
    /// <summary>窗口宽度。</summary>
    public uint XSize;
    /// <summary>窗口高度。</summary>
    public uint YSize;
    /// <summary>控制台列数。</summary>
    public uint XCountChars;
    /// <summary>控制台行数。</summary>
    public uint YCountChars;
    /// <summary>控制台属性。</summary>
    public uint FillAttribute;
    /// <summary>启动信息标记。</summary>
    public uint Flags;
    /// <summary>窗口显示方式。</summary>
    public ushort ShowWindow;
    /// <summary>系统保留区域长度。</summary>
    public ushort ReservedLength;
    /// <summary>系统保留区域指针。</summary>
    public nint ReservedPointer;
    /// <summary>标准输入句柄；ConPTY 模式不直接设置。</summary>
    public nint StandardInput;
    /// <summary>标准输出句柄。</summary>
    public nint StandardOutput;
    /// <summary>标准错误句柄。</summary>
    public nint StandardError;
}
