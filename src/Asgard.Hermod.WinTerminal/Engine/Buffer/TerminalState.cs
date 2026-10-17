// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine.Buffer;

/// <summary>保存光标、文字属性与终端工作模式。</summary>
public sealed class TerminalState
{
    /// <summary>光标列。</summary>
    public int CursorX;
    /// <summary>光标行。</summary>
    public int CursorY;
    /// <summary>待换行状态。</summary>
    public bool WrapPending;
    /// <summary>当前文字属性。</summary>
    public CharAttributes Attributes = CharAttributes.Default;
    /// <summary>终端工作模式。</summary>
    public TerminalModes Modes = new();
    /// <summary>保存的光标列。</summary>
    public int SavedCursorX;
    /// <summary>保存的光标行。</summary>
    public int SavedCursorY;
    /// <summary>保存的文字属性。</summary>
    public CharAttributes SavedAttributes = CharAttributes.Default;
    /// <summary>保存的原点模式。</summary>
    public bool SavedOriginMode;
    /// <summary>保存的待换行状态。</summary>
    public bool SavedWrapPending;
    /// <summary>保存光标与关联状态。</summary>
    public void SaveCursor()
    {
        SavedCursorX = CursorX;
        SavedCursorY = CursorY;
        SavedAttributes = Attributes;
        SavedOriginMode = Modes.OriginMode;
        SavedWrapPending = WrapPending;
    }

    /// <summary>恢复保存的光标与关联状态。</summary>
    public void RestoreCursor()
    {
        CursorX = SavedCursorX;
        CursorY = SavedCursorY;
        Attributes = SavedAttributes;
        Modes.OriginMode = SavedOriginMode;
        WrapPending = SavedWrapPending;
    }
}
