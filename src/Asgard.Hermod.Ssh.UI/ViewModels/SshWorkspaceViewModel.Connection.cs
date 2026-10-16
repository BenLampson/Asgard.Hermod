using System.Threading.Channels;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>串行管理连接与关闭，网络读写不占用 Dispatcher。</summary>
public sealed partial class SshWorkspaceViewModel
{
    /// <summary>失败不清空输出，只有新连接完成后才替换旧终端。</summary>
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_runTask is not null) await _runTask;
        if (IsClosed) return;
        _connecting?.Dispose();
        _connecting = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        Connection.Begin(_connecting.Cancel, HasConnectedOnce);
        _phase = SshWorkspacePhase.Connecting; Error = ""; ErrorCode = ""; Refresh();
        ISshConnection? opened = null;
        try
        {
            var options = await _configurations.GetConnectionAsync(InstanceId!, _connecting.Token)
                ?? throw Text.Exception("SshConnectionMissing");
            _profile = SshConnectionOptions.Parse(options); Refresh();
            opened = await _connections.OpenAsync(options, _terminal.Buffer.Columns, _terminal.Buffer.Rows, _connecting.Token);
            if (IsClosed || _connecting.IsCancellationRequested) { await opened.DisposeAsync(); opened = null; _connecting.Token.ThrowIfCancellationRequested(); return; }
            var previous = _terminal;
            _terminal = new TerminalSession(previous.Buffer.Columns, previous.Buffer.Rows);
            OnPropertyChanged(nameof(Session)); previous.Dispose();
            _input = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(64)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
            _ioCancellation?.Dispose(); _ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _inputOverflow = false; _connection = opened;
            _disconnectRequested = false;
            HasConnectedOnce = true; HostKeys = opened.HostKeys; _phase = SshWorkspacePhase.Connected; _connectedAt = DateTime.UtcNow;
            _terminal.InputReceived += OnInput; _terminal.SizeChanged += OnResize;
            _clock.Start(); Refresh();
            Connection.Finish();
            _runTask = ObserveAsync(opened, _terminal, _input, _ioCancellation);
            Assistant.ConnectionChanged();
            await Monitor.StartAsync(opened as ISshWorkspaceConnection);
        }
        catch (OperationCanceledException) when (_connecting.IsCancellationRequested || IsClosed)
        {
            if (opened is not null) await opened.DisposeAsync();
            Connection.Finish();
            if (!IsClosed) { _phase = SshWorkspacePhase.Disconnected; Error = Text.Get("SshConnectionCanceled"); Refresh(); }
        }
        catch (Exception error)
        {
            if (opened is not null) await opened.DisposeAsync();
            if (!IsClosed)
            {
                var safe = SshConnectionFactory.SafeError(error);
                Error = Text.Error(error, safe.ResourceKey); ErrorCode = safe.Code; _phase = SshWorkspacePhase.Failed;
                Connection.Finish(Error); Refresh();
            }
        }
    }

    /// <summary>输入回调只入队，满队列终止连接，不能静默丢字节或重放输入。</summary>
    private void OnInput(object? sender, ReadOnlyMemory<byte> input)
    {
        if (_input?.Writer.TryWrite(input) != true)
        { _inputOverflow = true; _ioCancellation?.Cancel(); }
    }

    /// <summary>尺寸通知由协议库排队；断线竞争不能把 SDK 异常抛向 WPF。</summary>
    private void OnResize(object? sender, Asgard.Hermod.WinTerminal.Engine.Model.TerminalSize size)
    {
        try { _connection?.Resize(size.Width, size.Height); }
        catch (Exception) { _ioCancellation?.Cancel(); }
        _dispatcher.BeginInvoke(() => { if (!IsClosed) OnPropertyChanged(nameof(GridSize)); });
    }

    /// <summary>两个独立异步循环搬运原始字节，终止时等待清理并保留显示缓冲区。</summary>
    private async Task ObserveAsync(ISshConnection connection, TerminalSession terminal,
        Channel<ReadOnlyMemory<byte>> input, CancellationTokenSource cancellation)
    {
        Exception? failure = null;
        var read = ReadAsync(); var write = WriteAsync();
        try
        {
            Task first = await Task.WhenAny(read, write).ConfigureAwait(false);
            await first.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { failure = error; }
        finally
        {
            cancellation.Cancel(); input.Writer.TryComplete();
            terminal.InputReceived -= OnInput; terminal.SizeChanged -= OnResize;
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
            try { await Task.WhenAll(read, write).ConfigureAwait(false); }
            catch (Exception error) when (cancellation.IsCancellationRequested) { if (error is not OperationCanceledException and not ObjectDisposedException) failure ??= error; }
        }
        if (!_dispatcher.HasShutdownStarted)
            await _dispatcher.InvokeAsync(async () =>
            {
                if (IsClosed || !ReferenceEquals(_connection, connection)) return;
                _connection = null; _clock.Stop();
                await StopWorkspaceOperationsAsync();
                if (_inputOverflow) { Error = Text.Get("SshInputOverflow"); ErrorCode = "SSH_INPUT_OVERFLOW"; }
                else if (failure is not null) { var safe = SshConnectionFactory.SafeError(failure); Error = Text.Error(safe); ErrorCode = safe.Code; }
                _phase = failure is null && !_inputOverflow ? SshWorkspacePhase.Disconnected : SshWorkspacePhase.Failed;
                if (_disconnectRequested) Connection.Finish();
                else Connection.Disconnected(EndedMessage);
                Refresh();
            }).Task.Unwrap();

        async Task ReadAsync()
        {
            byte[] buffer = new byte[32 * 1024];
            while (true)
            {
                int count = await connection.ReadAsync(buffer, cancellation.Token).ConfigureAwait(false);
                if (count == 0) break;
                terminal.FeedOutput(buffer.AsSpan(0, count));
            }
            await connection.GetExitCodeAsync(cancellation.Token).ConfigureAwait(false);
        }
        async Task WriteAsync()
        {
            await foreach (var bytes in input.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
                await connection.WriteAsync(bytes, cancellation.Token).ConfigureAwait(false);
        }
    }

    /// <summary>用户断开先停止传输，观察任务完成后继续保留终端缓冲区。</summary>
    private async Task DisconnectAsync()
    {
        _disconnectRequested = true;
        await StopWorkspaceOperationsAsync();
        _ioCancellation?.Cancel();
        if (_connection is { } connection) await connection.DisposeAsync();
        if (_runTask is not null) await _runTask;
    }

    /// <inheritdoc />
    protected override async ValueTask ReleaseAsync()
    {
        OnPropertyChanged(nameof(Session)); _clock.Stop(); _saveFeedbackTimer.Stop(); _lifetime.Cancel();
        await StopWorkspaceOperationsAsync(); Files.Dispose(); Assistant.Dispose(); DisposeLogs();
        Connection.Finish();
        _connecting?.Cancel(); _ioCancellation?.Cancel();
        if (_connection is { } connection) await connection.DisposeAsync();
        if (_connectTask is not null) await _connectTask;
        if (_runTask is not null) await _runTask;
        _terminal.Dispose(); _connecting?.Dispose(); _ioCancellation?.Dispose(); _lifetime.Dispose();
        Refresh();
    }
}
