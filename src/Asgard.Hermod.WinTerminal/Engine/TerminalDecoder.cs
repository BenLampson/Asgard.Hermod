// 基于 Rikitav/VirtualTerminal，Apache-2.0；来源见 THIRD-PARTY-NOTICES.md。
// Hermod 修改：限定 Windows/WPF，调整命名空间、注释和文件组织。
namespace Asgard.Hermod.WinTerminal.Engine;

/// <summary>将 VT/ANSI 解析事件应用到终端缓冲区、光标、样式和工作模式。</summary>
public sealed partial class TerminalDecoder : IVtHandler, ITerminalDecoder
{
    /// <summary>VT 字节解析器。</summary>
    private readonly VtDecoder _parser;
    /// <summary>登记的超链接地址。</summary>
    private readonly List<string> _hyperlinks = [];
    /// <summary>等待落实的回车。</summary>
    private bool _pendingReturn;
    /// <summary>等待落实的换行数。</summary>
    private int _pendingLineFeeds;
    /// <summary>屏幕缓冲区。</summary>
    public TerminalScreenBuffer Buffer { get; }
    /// <summary>终端状态。</summary>
    public TerminalState State { get; } = new();
    /// <summary>终端选项。</summary>
    public TerminalOptions Options { get; set; }
    /// <summary>窗口标题。</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>标题变化事件。</summary>
    public event EventHandler<string>? TitleChanged;
    /// <summary>响铃事件。</summary>
    public event EventHandler? Bell;
    /// <summary>宿主通知事件。</summary>
    public event EventHandler<string>? NotificationReceived;
    /// <summary>OSC 剪贴板请求事件。</summary>
    public event EventHandler<ClipboardRequestEventArgs>? ClipboardRequested;
    /// <summary>Shell 集成标记事件。</summary>
    public event EventHandler<string>? ShellIntegrationMark;
    /// <summary>iTerm2 注释事件。</summary>
    public event EventHandler<string>? Iterm2Annotation;
    /// <summary>终端查询回复回调。</summary>
    public Action<ReadOnlySpan<byte>>? SendOutput { get; set; }
    /// <summary>光标位置。</summary>
    public TerminalPoint CursorPosition => new(State.CursorX, State.CursorY);
    /// <summary>字节编码。</summary>
    public Encoding Encoding => _parser.Encoding;

    /// <summary>初始化 TerminalDecoder 实例。</summary>
    public TerminalDecoder(TerminalScreenBuffer? buffer = null, TerminalOptions? options = null)
    {
        Buffer = buffer ?? new TerminalScreenBuffer(128, 20);
        Options = options ?? new TerminalOptions();
        Buffer.SetScrollbackMax(Options.ScrollbackMaxLines);
        _parser = new VtDecoder(this);
    }

    /// <summary>增量处理输入字节，保留跨数据块的解析状态。</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;
        lock (Buffer.SyncRoot)
            _parser.Write(data);
    }

    /// <summary>调整缓冲区尺寸并约束光标位置。</summary>
    public void Resize(ushort columns, ushort rows) => Resize(columns, rows, pushScrollback: true);
    /// <summary>调整缓冲区尺寸并约束光标位置。</summary>
    public void Resize(ushort columns, ushort rows, bool pushScrollback)
    {
        lock (Buffer.SyncRoot)
        {
            FlushPendingNewline();
            Buffer.Resize(columns, rows, State, pushScrollback);
        }
    }

    /// <summary>释放缓冲区、解析器或字体缓存资源。</summary>
    public void Dispose() => _parser.Dispose();
    /// <summary>处理 GetHyperlink 对应的终端协议或渲染逻辑。</summary>
    internal string? GetHyperlink(byte id) => id == 0 || id > _hyperlinks.Count ? null : _hyperlinks[id - 1];
    /// <summary>处理 RegisterHyperlink 对应的终端协议或渲染逻辑。</summary>
    internal byte RegisterHyperlink(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return 0;
        int idx = _hyperlinks.IndexOf(uri);
        if (idx < 0)
        {
            if (_hyperlinks.Count >= 255)
                return 0;
            _hyperlinks.Add(uri);
            idx = _hyperlinks.Count - 1;
        }

        return (byte)(idx + 1);
    }

    /// <summary>处理 SetTitle 对应的终端协议或渲染逻辑。</summary>
    internal void SetTitle(string title)
    {
        Title = title;
        TitleChanged?.Invoke(this, title);
    }

    /// <summary>处理 RaiseBell 对应的终端协议或渲染逻辑。</summary>
    private void RaiseBell() => Bell?.Invoke(this, EventArgs.Empty);
    /// <summary>处理 RaiseNotification 对应的终端协议或渲染逻辑。</summary>
    internal void RaiseNotification(string message) => NotificationReceived?.Invoke(this, message);
    /// <summary>处理 RaiseClipboardRequested 对应的终端协议或渲染逻辑。</summary>
    internal void RaiseClipboardRequested(string selection, string? data) => ClipboardRequested?.Invoke(this, new ClipboardRequestEventArgs(selection, data));
    /// <summary>处理 RaiseShellIntegrationMark 对应的终端协议或渲染逻辑。</summary>
    internal void RaiseShellIntegrationMark(string arguments) => ShellIntegrationMark?.Invoke(this, arguments);
    /// <summary>处理 RaiseIterm2Annotation 对应的终端协议或渲染逻辑。</summary>
    internal void RaiseIterm2Annotation(string arguments) => Iterm2Annotation?.Invoke(this, arguments);
    /// <summary>处理 Respond 对应的终端协议或渲染逻辑。</summary>
    private void Respond(string response)
    {
        if (SendOutput is null)
            return;
        int byteCount = Encoding.UTF8.GetByteCount(response);
        if (byteCount == 0)
            return;
        const int StackAllocThreshold = 512;
        if (byteCount <= StackAllocThreshold)
        {
            Span<byte> bytes = stackalloc byte[byteCount];
            Encoding.UTF8.GetBytes(response, bytes);
            SendOutput(bytes);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            Encoding.UTF8.GetBytes(response, rented);
            SendOutput(rented.AsSpan(0, byteCount));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>写入 Unicode 字符并更新光标与脏行。</summary>
    public void Print(Rune rune)
    {
        Rune single = rune;
        Print(MemoryMarshal.CreateReadOnlySpan(in single, 1));
    }

    /// <summary>写入 Unicode 字符并更新光标与脏行。</summary>
    public void Print(scoped ReadOnlySpan<Rune> runes)
    {
        if (runes.IsEmpty)
            return;
        try
        {
            FlushPendingNewline();
            int cols = Buffer.Columns;
            CellRenderStyle style = State.Attributes.ToCellStyle(Options.Palette, Options.BoldAsBright);
            bool insertMode = State.Modes.InsertMode;
            int lastDirtyRow = -1;
            foreach (Rune rune in runes)
            {
                int width = Wcwidth.Of(rune);
                if (width < 0)
                    continue;
                if (width == 0)
                    width = 1;
                if (State.WrapPending)
                {
                    State.CursorX = 0;
                    Index();
                    State.WrapPending = false;
                }

                int cx = State.CursorX;
                int cy = State.CursorY;
                if (cx + width > cols)
                {
                    if (State.Modes.AutoWrap && width <= cols)
                    {
                        State.CursorX = 0;
                        Index();
                        cx = 0;
                        cy = State.CursorY;
                    }
                    else
                    {
                        width = Math.Max(1, cols - cx);
                    }
                }

                if (insertMode)
                    Buffer.InsertChars(cy, cx, width);
                ref TerminalCellInfo cell = ref Buffer.GetCell(cy, cx);
                CellStyleFlags flags = style.Flags;
                if (width == 2)
                    flags |= CellStyleFlags.Wide;
                cell = new TerminalCellInfo(rune, style with { Flags = flags });
                if (width == 2 && cx + 1 < cols)
                {
                    ref TerminalCellInfo cont = ref Buffer.GetCell(cy, cx + 1);
                    cont = new TerminalCellInfo(new Rune(' '), style with { Flags = style.Flags | CellStyleFlags.Continuation });
                }

                if (cy != lastDirtyRow)
                {
                    Buffer.MarkRowDirty(cy);
                    lastDirtyRow = cy;
                }

                if (cx + width < cols)
                {
                    State.CursorX = cx + width;
                    State.WrapPending = false;
                }
                else
                {
                    State.CursorX = cols - 1;
                    State.WrapPending = State.Modes.AutoWrap;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TerminalDecoder.Print failed for a run of {runes.Length} runes.\n{ex}");
        }
    }

    /// <summary>执行控制字符指令。</summary>
    public void Execute(byte control)
    {
        switch (control)
        {
            case 0x0A:
            case 0x0B:
            case 0x0C:
                _pendingLineFeeds++;
                break;
            case 0x0D:
                _pendingReturn = true;
                break;
            case 0x07:
                FlushPendingNewline();
                RaiseBell();
                break;
            case 0x08:
                FlushPendingNewline();
                CursorLeft();
                break;
            case 0x09:
                FlushPendingNewline();
                Tab();
                break;
            default:
                break;
        }
    }

    /// <summary>落实暂存的 CR/LF；会话在一次输出结束后调用，使光标立即反映完整输入。</summary>
    internal void FlushPendingNewline()
    {
        if (!_pendingReturn && _pendingLineFeeds == 0)
            return;
        bool cr = _pendingReturn;
        int lfs = _pendingLineFeeds;
        _pendingReturn = false;
        _pendingLineFeeds = 0;
        if (cr)
            CarriageReturn();
        for (int i = 0; i < lfs; i++)
            LineFeed();
    }
}
