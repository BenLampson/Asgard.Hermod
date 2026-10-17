// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Vt;

/// <summary>携带 OSC 剪贴板请求；宿主决定是否处理。</summary>
public sealed class ClipboardRequestEventArgs : EventArgs
{
    /// <summary>初始化 ClipboardRequestEventArgs 实例。</summary>
    public ClipboardRequestEventArgs(string selection, string? data)
    {
        Selection = selection;
        Data = data;
    }

    /// <summary>保存或提供 Selection 对应的终端状态。</summary>
    public string Selection { get; }
    /// <summary>保存或提供 Data 对应的终端状态。</summary>
    public string? Data { get; }
    /// <summary>保存或提供 IsQuery 对应的终端状态。</summary>
    public bool IsQuery => Data is null;
}
