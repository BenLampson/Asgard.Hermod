// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Buffer;

/// <summary>表示 MouseTrackingMode 的终端协议数据。</summary>
public enum MouseTrackingMode
{
    Off,
    X10,
    ButtonEvent,
    AnyEvent,
}
