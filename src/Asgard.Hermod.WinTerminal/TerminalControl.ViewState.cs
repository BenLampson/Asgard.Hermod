using System.Runtime.CompilerServices;

namespace Asgard.Hermod.WinTerminal;

/// <summary>共用显示控件切换 PTY 时，弱引用保存各缓冲区的滚动、选区与搜索状态。</summary>
public sealed partial class TerminalControl
{
    private readonly ConditionalWeakTable<TerminalSession, SavedViewState> _viewStates = new();
    private sealed class SavedViewState
    {
        internal TerminalSelection? Selection;
        internal int ScrollOffset;
        internal string SearchQuery = "";
        internal int SearchIndex = -1;
        internal double FontSize;
        internal double LineHeight;
        internal long Revision;
        internal int HistoryCount;
    }
    private void SaveViewState(TerminalSession session)
    {
        if (session.IsDisposed || _disposed) return;
        var state = _viewStates.GetValue(session, static _ => new());
        state.Selection = _selection; state.ScrollOffset = _scrollOffset; state.SearchQuery = _searchQuery; state.SearchIndex = _searchIndex; state.FontSize = FontSize; state.LineHeight = LineHeight;
        lock (session.Buffer.SyncRoot) { state.Revision = session.Revision; state.HistoryCount = session.Buffer.ScrollbackCount; }
    }
    private void RestoreViewState(TerminalSession? session)
    {
        _selection = null; _scrollOffset = 0; _searchQuery = ""; _searchIndex = -1;
        if (session is null || !_viewStates.TryGetValue(session, out var state)) return;
        lock (session.Buffer.SyncRoot)
        {
            _selection = state.Revision == session.Revision ? state.Selection : null;
            _scrollOffset = state.ScrollOffset > 0 ? Math.Max(0, state.ScrollOffset + session.Buffer.ScrollbackCount - state.HistoryCount) : 0;
        }
        _searchQuery = state.SearchQuery; _searchIndex = state.SearchIndex;
        if (state.FontSize > 0) SetCurrentValue(FontSizeProperty, state.FontSize);
        SetCurrentValue(LineHeightProperty, state.LineHeight);
    }
}
