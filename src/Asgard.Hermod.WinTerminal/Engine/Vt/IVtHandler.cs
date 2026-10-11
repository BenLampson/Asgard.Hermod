// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Vt;

/// <summary>接收解析器输出的文字、控制字符与转义指令。</summary>
public interface IVtHandler
{
    /// <summary>写入 Unicode 字符并更新光标与脏行。</summary>
    void Print(Rune rune);
    /// <summary>写入 Unicode 字符并更新光标与脏行。</summary>
    void Print(scoped ReadOnlySpan<Rune> runes)
    {
        foreach (Rune rune in runes)
            Print(rune);
    }

    /// <summary>执行控制字符指令。</summary>
    void Execute(byte control);
    /// <summary>执行 ESC 指令。</summary>
    void EscDispatch(byte final, ReadOnlySpan<byte> intermediates);
    /// <summary>执行 CSI 指令。</summary>
    void CsiDispatch(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker);
    /// <summary>执行 OSC 指令。</summary>
    void OscDispatch(ReadOnlySpan<byte> data);
    /// <summary>开始接收 DCS 负载。</summary>
    void DcsHook(byte final, ReadOnlySpan<byte> intermediates, in CsiParams parameters, char privateMarker);
    /// <summary>接收 DCS 负载。</summary>
    void DcsPut(byte b);
    /// <summary>结束 DCS 负载。</summary>
    void DcsUnhook();
    /// <summary>处理 SosPmApcDispatch 对应的终端协议或渲染逻辑。</summary>
    void SosPmApcDispatch(byte kind, ReadOnlySpan<byte> data);
}
