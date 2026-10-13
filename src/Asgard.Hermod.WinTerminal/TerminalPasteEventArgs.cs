namespace Asgard.Hermod.WinTerminal;

/// <summary>宿主可在原始文本发送前确认多行粘贴；终端库不负责业务对话框。</summary>
public sealed class TerminalPasteEventArgs(string text) : EventArgs
{
    /// <summary>原始剪贴板内容，不翻译、不规范化换行。</summary>
    public string Text { get; } = text;
    /// <summary>取消后不发送任何文本。</summary>
    public bool Cancel { get; set; }
}
