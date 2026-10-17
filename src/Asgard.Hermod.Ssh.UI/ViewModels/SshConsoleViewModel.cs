using System.Globalization;
using System.Threading.Channels;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>一个控制台独立拥有 PTY、输入队列和终端缓冲；不拥有实例传输或工具任务。</summary>
public sealed class SshConsoleViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ISshConsoleConnection _connection;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<ReadOnlyMemory<byte>> _input = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
    private Task _runTask = Task.CompletedTask;
    private bool _overflow, _started, _disposed;

    /// <summary>Shell 句柄只能由此控制台释放，助手也固定绑定此来源。</summary>
    public SshConsoleViewModel(string name, ISshConsoleConnection connection, SshAssistantViewModel assistant, int columns, int rows)
    { Name = name; _connection = connection; Assistant = assistant; Session = new(columns, rows); }
    /// <summary>用户可见控制台名称。</summary>
    public string Name { get; }
    /// <summary>本机搜索词随控制台保存。</summary>
    public string SearchText { get; set => SetProperty(ref field, value); } = "";
    /// <summary>搜索面板的展开状态只属于此控制台。</summary>
    public bool IsSearchOpen { get; set => SetProperty(ref field, value); }
    /// <summary>共用标签条显示名称。</summary>
    public string Header => Name;
    /// <summary>共用标签条使用的稳定图标。</summary>
    public string Icon => "terminal";
    /// <summary>终端没有待保存脚本文档。</summary>
    public bool HasUnsavedChanges => false;
    /// <summary>此控制台独立的完整终端缓冲。</summary>
    public TerminalSession Session { get; }
    /// <summary>此控制台的独立助手历史和草稿。</summary>
    public SshAssistantViewModel Assistant { get; }
    /// <summary>只有活跃 Shell 可输入。</summary>
    public bool IsRunning { get; private set => SetProperty(ref field, value); } = true;
    /// <summary>结束后保留选择、复制与输出。</summary>
    public bool IsReadOnly => !IsRunning || _disposed;
    /// <summary>标签区明确区分仍可输入和只保留历史的控制台。</summary>
    public bool IsEnded => !IsRunning;
    /// <summary>安全的控制台结束说明。</summary>
    public string Error { get; private set; } = "";
    /// <summary>客户端输入队列满时只停止此控制台。</summary>
    public bool InputOverflow => _overflow;
    /// <summary>终止或网格改变时通知外层刷新状态，不共享字节队列。</summary>
    public event EventHandler? StateChanged;
    /// <summary>加入集合以后才启动读取，避免同步 EOF 早于标签创建。</summary>
    internal void Start()
    {
        if (_started) return; _started = true;
        Session.InputReceived += OnInput; Session.SizeChanged += OnResize; _runTask = ObserveAsync();
    }
    /// <summary>实际 PTY 网格数字不受翻译与区域设置影响。</summary>
    public string GridSize => Session.Buffer.Columns.ToString(CultureInfo.InvariantCulture) + " × " + Session.Buffer.Rows.ToString(CultureInfo.InvariantCulture);

    private void OnInput(object? sender, ReadOnlyMemory<byte> bytes)
    {
        if (IsReadOnly) return;
        // 调用者可能复用原输入内存，因此队列保留独立字节；容量满不能丢字或重放。
        if (!_input.Writer.TryWrite(bytes.ToArray())) { _overflow = true; _lifetime.Cancel(); }
    }
    private void OnResize(object? sender, Asgard.Hermod.WinTerminal.Engine.Model.TerminalSize size)
    {
        if (!IsRunning) return;
        try { _connection.Resize(size.Width, size.Height); }
        catch (Exception) { _lifetime.Cancel(); }
        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }
    private async Task ObserveAsync()
    {
        Exception? failure = null; var read = ReadAsync(); var write = WriteAsync();
        try { await await Task.WhenAny(read, write).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (_lifetime.IsCancellationRequested) { _ = error; }
        catch (Exception error) { failure = error; }
        finally
        {
            _lifetime.Cancel(); _input.Writer.TryComplete(); Session.InputReceived -= OnInput; Session.SizeChanged -= OnResize;
            try { await _connection.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure ??= error; }
            try { await Task.WhenAll(read, write).ConfigureAwait(false); } catch (Exception) when (_lifetime.IsCancellationRequested) { }
        }
        if (!_dispatcher.HasShutdownStarted)
            await _dispatcher.InvokeAsync(() =>
            {
                IsRunning = false; Error = _overflow ? Text.Get("SshInputOverflow") : failure is null ? "" : Text.Error(SshConnectionFactory.SafeError(failure));
                OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(IsEnded)); Assistant.CloseSource(); StateChanged?.Invoke(this, EventArgs.Empty);
            });

        async Task ReadAsync()
        {
            byte[] buffer = new byte[32 * 1024];
            while (true) { int count = await _connection.ReadAsync(buffer, _lifetime.Token).ConfigureAwait(false); if (count == 0) break; Session.FeedOutput(buffer.AsSpan(0, count)); }
            await _connection.GetExitCodeAsync(_lifetime.Token).ConfigureAwait(false);
        }
        async Task WriteAsync()
        {
            await foreach (var bytes in _input.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false)) await _connection.WriteAsync(bytes, _lifetime.Token).ConfigureAwait(false);
        }
    }
    /// <summary>实例断线或用户关闭 Shell 时停止 I/O，但保留历史缓冲和助手记录。</summary>
    internal async Task StopAsync()
    {
        _lifetime.Cancel(); _input.Writer.TryComplete();
        try { await _connection.DisposeAsync(); } catch (Exception) { }
        await _runTask;
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        await StopAsync(); Assistant.Dispose(); Session.Dispose(); _lifetime.Dispose();
    }
}
