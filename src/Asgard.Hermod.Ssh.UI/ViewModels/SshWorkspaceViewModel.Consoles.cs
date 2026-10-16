using System.Collections.ObjectModel;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

public sealed partial class SshWorkspaceViewModel
{
    private int _consoleNumber;
    /// <summary>实例窗口中各自独立的 PTY 标签；允许关闭最后一个。</summary>
    public ObservableCollection<SshConsoleViewModel> Consoles { get; } = [];
    /// <summary>切换只切换本机显示，不改变其他 Shell、目录或工具任务。</summary>
    public SshConsoleViewModel? ActiveConsole
    {
        get; set
        {
            if (value is not null && !Consoles.Contains(value) || !SetProperty(ref field, value)) return;
            foreach (string property in new[] { nameof(Session), nameof(Assistant), nameof(HasActiveConsole), nameof(IsTerminalSection), nameof(ActiveConsoleLabel), nameof(IsReadOnly), nameof(GridSize) }) OnPropertyChanged(property);
            ToggleAgentCommand?.NotifyCanExecuteChanged();
        }
    }
    /// <summary>零控制台时仍保留实例工具。</summary>
    public bool HasActiveConsole => ActiveConsole is not null;
    /// <summary>状态栏指明当前控制台，不把工具归属混入 Shell。</summary>
    public string ActiveConsoleLabel => ActiveConsole is { } console ? Text.Format(console.IsRunning ? "SshActiveConsole" : "SshEndedConsole", console.Name) : Text.Get("SshNoConsole");
    /// <summary>新增 Shell 期间保留其他可输入的控制台。</summary>
    public bool IsCreatingConsole { get; private set { if (SetProperty(ref field, value)) { NewSessionCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(ConsoleActionStatus)); } } }
    /// <summary>失败只在控制台条显示安全说明，不替换实例连接状态。</summary>
    public string ConsoleError { get; private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(ConsoleActionStatus)); } } = "";
    /// <summary>控制台创建状态与错误互斥。</summary>
    public string ConsoleActionStatus => IsCreatingConsole ? Text.Get("SshConsoleCreating") : ConsoleError;
    /// <summary>关闭一个 Shell 前独立确认。</summary>
    public AsyncRelayCommand CloseConsoleCommand { get; }

    private SshConsoleViewModel AddOpenedConsole(ISshConsoleConnection handle, int columns, int rows)
    {
        var owner = _connection; string host = RemoteIdentity; SshConsoleViewModel? console = null;
        var assistant = new SshAssistantViewModel(_agentClient, () => console?.IsRunning == true && IsConnected && ReferenceEquals(owner, _connection) ? owner as ISshWorkspaceConnection : null, () => host, _dialogs);
        console = new(Text.Format("SshConsoleName", ++_consoleNumber), handle, assistant, columns, rows);
        console.StateChanged += (_, _) =>
        {
            // 旧单 Shell 适配器没有独立传输信号；生产连接只通过 Disconnected 判断实例断线。
            if (!console.IsRunning && ReferenceEquals(owner, _connection) && owner?.Disconnected.CanBeCanceled == false) _ioCancellation?.Cancel();
            if (ActiveConsole == console) Refresh();
        };
        Consoles.Add(console); ActiveConsole = console; console.Start(); CloseConsoleCommand.NotifyCanExecuteChanged(); return console;
    }
    private async Task AddConsoleAsync()
    {
        if (_connection is not { } connection || !IsConnected || IsCreatingConsole) return;
        var token = _ioCancellation?.Token ?? _lifetime.Token;
        IsCreatingConsole = true; ConsoleError = "";
        try
        {
            int columns = Session?.Buffer.Columns ?? 80, rows = Session?.Buffer.Rows ?? 24;
            var opened = await connection.OpenConsoleAsync(columns, rows, token);
            if (IsClosed || !IsConnected || !ReferenceEquals(connection, _connection)) { await opened.DisposeAsync(); return; }
            AddOpenedConsole(opened, columns, rows);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ConsoleError = Text.Error(error, "SshConsoleCreateFailed"); }
        finally { IsCreatingConsole = false; }
    }
    private async Task CloseConsoleAsync(SshConsoleViewModel? console)
    {
        if (console is null || !Consoles.Contains(console)) return;
        if (console.IsRunning && !await _dialogs.ConfirmConsoleCloseAsync(Name, console.Name)) return;
        if (!Consoles.Contains(console) || IsClosed) return;
        int index = Consoles.IndexOf(console); bool wasActive = ActiveConsole == console; Consoles.Remove(console);
        if (wasActive) ActiveConsole = Consoles.Count == 0 ? null : Consoles[Math.Min(index, Consoles.Count - 1)];
        await console.DisposeAsync(); CloseConsoleCommand.NotifyCanExecuteChanged();
    }
    private async Task StopConsolesAsync()
    {
        foreach (var console in Consoles.ToArray()) await console.StopAsync();
    }
}
