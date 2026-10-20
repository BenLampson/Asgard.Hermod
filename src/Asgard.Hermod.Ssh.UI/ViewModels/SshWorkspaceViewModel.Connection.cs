namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>实例传输与每个 PTY 分别结束；断线后所有旧缓冲区保持只读。</summary>
public sealed partial class SshWorkspaceViewModel
{
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_runTask is not null) await _runTask;
        if (IsClosed) return;
        _connecting?.Dispose();
        _connecting = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        Connection.Begin(_connecting.Cancel, HasConnectedOnce);
        _phase = SshWorkspacePhase.Connecting; Error = ""; ErrorCode = ""; ConsoleError = ""; Refresh();
        ISshConnection? opened = null;
        try
        {
            var options = await _configurations.GetConnectionAsync(InstanceId!, _connecting.Token) ?? throw Text.Exception("SshConnectionMissing");
            _profile = SshConnectionOptions.Parse(options); Refresh();
            int columns = Session?.Buffer.Columns ?? 80, rows = Session?.Buffer.Rows ?? 24;
            opened = await _connections.OpenAsync(options, columns, rows, _connecting.Token);
            if (IsClosed || _connecting.IsCancellationRequested) { await opened.DisposeAsync(); opened = null; _connecting.Token.ThrowIfCancellationRequested(); return; }
            _ioCancellation?.Dispose(); _ioCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _connection = opened; _disconnectRequested = false;
            HasConnectedOnce = true; HostKeys = opened.HostKeys; _phase = SshWorkspacePhase.Connected; _connectedAt = DateTime.UtcNow;
            AddOpenedConsole(opened.InitialConsole, columns, rows);
            _clock.Start(); Refresh(); Connection.Finish();
            _runTask = ObserveTransportAsync(opened, _ioCancellation);
            await Monitor.StartAsync(opened as ISshWorkspaceConnection);
            if (IsDockerCommandsOpen) await DockerCommands.InitializeAsync();
        }
        catch (OperationCanceledException) when (_connecting.IsCancellationRequested || IsClosed)
        {
            if (opened is not null) await opened.DisposeAsync(); Connection.Finish();
            if (!IsClosed) { _phase = SshWorkspacePhase.Disconnected; Error = Text.Get("SshConnectionCanceled"); Refresh(); }
        }
        catch (Exception error)
        {
            if (opened is not null) await opened.DisposeAsync();
            if (!IsClosed)
            {
                var safe = SshConnectionFactory.SafeError(error); Error = Text.Error(error, safe.ResourceKey); ErrorCode = safe.Code; _phase = SshWorkspacePhase.Failed;
                Connection.Finish(Error); Refresh();
            }
        }
    }

    private async Task ObserveTransportAsync(ISshConnection connection, CancellationTokenSource cancellation)
    {
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, connection.Disconnected);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stopped.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        cancellation.Cancel();
        try { await connection.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
        if (!_dispatcher.HasShutdownStarted)
            await _dispatcher.InvokeAsync(async () =>
            {
                if (IsClosed || !ReferenceEquals(_connection, connection)) return;
                _connection = null; _clock.Stop();
                await StopConsolesAsync(); await StopWorkspaceOperationsAsync();
                _phase = SshWorkspacePhase.Disconnected;
                if (_disconnectRequested) Connection.Finish(); else Connection.Disconnected(EndedMessage);
                Refresh();
            }).Task.Unwrap();
    }

    private async Task DisconnectAsync()
    {
        _disconnectRequested = true; await StopWorkspaceOperationsAsync(); _ioCancellation?.Cancel();
        if (_connection is { } connection) await connection.DisposeAsync();
        if (_runTask is not null) await _runTask;
    }

    /// <inheritdoc />
    protected override async ValueTask ReleaseAsync()
    {
        OnPropertyChanged(nameof(Session)); _clock.Stop(); _saveFeedbackTimer.Stop(); _lifetime.Cancel(); _connecting?.Cancel(); _ioCancellation?.Cancel();
        await StopWorkspaceOperationsAsync(); Files.Dispose(); DisposeLogs();
        DockerCommands.Dispose();
        if (_connection is { } connection) await connection.DisposeAsync();
        if (_connectTask is not null) await _connectTask;
        if (_runTask is not null) await _runTask;
        await _consoleOpenTask;
        foreach (var console in Consoles.ToArray()) await console.DisposeAsync();
        Consoles.Clear(); ActiveConsole = null; _emptyAssistant.Dispose(); Connection.Finish();
        _connecting?.Dispose(); _ioCancellation?.Dispose(); _lifetime.Dispose(); Refresh();
    }
}
