namespace Asgard.Hermod.WinTerminal.Local;

/// <summary>与 Win32 STARTUPINFOEXW 保持布局一致的扩展信息。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeStartupInformationEx
{
    /// <summary>基础启动信息。</summary>
    public NativeStartupInformation StartupInfo;

    /// <summary>伪控制台关联属性列表。</summary>
    public nint AttributeList;
}
