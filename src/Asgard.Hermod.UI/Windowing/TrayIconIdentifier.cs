using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>按稳定 GUID 获取真实通知图标矩形。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TrayIconIdentifier
{
    internal int Size;
    internal nint Window;
    internal uint Id;
    internal Guid Guid;
}
