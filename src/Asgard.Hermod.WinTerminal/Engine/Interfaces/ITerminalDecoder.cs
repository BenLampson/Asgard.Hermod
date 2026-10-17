// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Interfaces;

/// <summary>定义终端解码器的字节输入、编码与尺寸调整能力。</summary>
public interface ITerminalDecoder : IDisposable
{
    /// <summary>字节编码。</summary>
    Encoding Encoding { get; }

    /// <summary>增量处理输入字节，保留跨数据块的解析状态。</summary>
    void Write(ReadOnlySpan<byte> data);
}
