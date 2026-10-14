using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>完整 Unicode NOTIFYICONDATA，GUID 在通知区域重建后保持不变。</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct TrayNotifyData
{
    internal int Size;
    internal nint Window;
    internal uint Id;
    internal uint Flags;
    internal uint Callback;
    internal nint Icon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
    internal uint State;
    internal uint StateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
    internal uint Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
    internal uint InfoFlags;
    internal Guid Guid;
    internal nint BalloonIcon;
}
