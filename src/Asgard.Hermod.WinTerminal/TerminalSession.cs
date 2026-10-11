namespace Asgard.Hermod.WinTerminal;

/// <summary>
/// 持有终端状态并连接字节输入、输出与尺寸变化；不依赖具体网络或进程协议。
/// </summary>
public sealed class TerminalSession : IDisposable
{
    private bool _disposed;
    private long _revision;

    /// <summary>输出改变缓冲区后触发；回调可能发生在后台线程。</summary>
    public event EventHandler? BufferUpdated;

    /// <summary>
    /// 用户输入或终端查询回复；数据拥有独立存储，连接器应排队发送，避免阻塞调用线程。
    /// </summary>
    public event EventHandler<ReadOnlyMemory<byte>>? InputReceived;

    /// <summary>网格尺寸改变后触发；连接器将其转发到 PTY。</summary>
    public event EventHandler<TerminalSize>? SizeChanged;

    /// <summary>会话的屏幕与历史缓冲区；直接访问时须持有 SyncRoot。</summary>
    public TerminalScreenBuffer Buffer { get; }

    /// <summary>会话独立持有的 VT/ANSI 解码器。</summary>
    public TerminalDecoder Decoder { get; }

    /// <summary>判断会话是否已经释放。</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed);

    /// <summary>输出或尺寸变化的版本号，供控件合并重绘。</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>创建会话；外部连接器负责自身连接的开启与释放。</summary>
    public TerminalSession(int columns = 80, int rows = 24, TerminalOptions? options = null)
    {
        ValidateSize(columns, rows);
        Buffer = new TerminalScreenBuffer(columns, rows);
        Decoder = new TerminalDecoder(Buffer, options ?? new TerminalOptions
        {
            AllowClipboard = false,
            FontFallback = ["Microsoft YaHei UI", "Segoe UI Emoji"],
        });
        Decoder.SendOutput = SendInput;
    }

    /// <summary>将连接器输出的原始 UTF-8/VT 字节送入解析器，不进行分块字符串转码。</summary>
    public void FeedOutput(ReadOnlySpan<byte> data)
    {
        lock (Buffer.SyncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (data.IsEmpty)
                return;
            Decoder.Write(data);
            // 重绘已有定时合并；每次输出完成后落实末尾换行，避免光标停在旧行。
            Decoder.FlushPendingNewline();
            Interlocked.Increment(ref _revision);
        }
        BufferUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>发送用户输入或解析器生成的查询回复。</summary>
    public void SendInput(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!data.IsEmpty)
            InputReceived?.Invoke(this, data.ToArray());
    }

    /// <summary>调整本地网格并通知连接器；ConPTY 的重排由系统完成。</summary>
    public void Resize(int columns, int rows, bool pushScrollback = false)
    {
        ValidateSize(columns, rows);
        lock (Buffer.SyncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Buffer.Columns == columns && Buffer.Rows == rows)
                return;
            Decoder.Resize((ushort)columns, (ushort)rows, pushScrollback);
            Interlocked.Increment(ref _revision);
        }
        SizeChanged?.Invoke(this, new TerminalSize(columns, rows));
        BufferUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>导出当前主屏幕或备用屏幕的纯文本。</summary>
    public string GetVisibleText()
    {
        lock (Buffer.SyncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Buffer.GetVisibleText();
        }
    }

    /// <summary>导出历史行与当前屏幕的纯文本。</summary>
    public string GetAllText()
    {
        lock (Buffer.SyncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Buffer.GetAllText();
        }
    }

    /// <summary>限制单次网格分配；Windows COORD 使用有符号 16 位坐标。</summary>
    private static void ValidateSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, 4096);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)columns * rows, 1_048_576);
    }

    /// <summary>释放本会话；宿主先停止连接器，再释放会话与控件。</summary>
    public void Dispose()
    {
        lock (Buffer.SyncRoot)
        {
            if (_disposed)
                return;
            _disposed = true;
            Decoder.SendOutput = null;
            Decoder.Dispose();
            Buffer.Dispose();
            BufferUpdated = null;
            InputReceived = null;
            SizeChanged = null;
        }
    }
}
