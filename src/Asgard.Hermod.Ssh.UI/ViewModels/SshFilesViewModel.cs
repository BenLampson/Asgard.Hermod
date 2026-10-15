using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Data;
using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>当前 SSH 连接上的文件浏览与串行传输；关闭分区不取消任务，工作区关闭会取消。</summary>
public sealed class SshFilesViewModel : ObservableObject, IDisposable
{
    private readonly Func<ISshWorkspaceConnection?> _connection;
    private readonly ISshWorkspaceDialogs _dialogs;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private long _revision;

    /// <summary>延迟读取当前连接，避免重连后引用已释放的传输。</summary>
    public SshFilesViewModel(Func<ISshWorkspaceConnection?> connection, ISshWorkspaceDialogs dialogs)
    {
        _connection = connection; _dialogs = dialogs;
        VisibleEntries = CollectionViewSource.GetDefaultView(Entries);
        VisibleEntries.Filter = value => value is SshFileEntry item && item.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase);
        RefreshCommand = new AsyncRelayCommand(_ => RunAsync(LoadAsync), _ => !IsBusy && !_disposed);
        ParentCommand = new AsyncRelayCommand(_ => { Path = SshRemotePath.Parent(Path); return RunAsync(LoadAsync); }, _ => !IsBusy && !_disposed);
        UploadCommand = new AsyncRelayCommand(_ => UploadAsync(), _ => !IsBusy && !_disposed);
        DownloadCommand = new AsyncRelayCommand(_ => DownloadAsync(), _ => !IsBusy && Selected is { IsDirectory: false } && !_disposed);
        CreateDirectoryCommand = new AsyncRelayCommand(_ => CreateAsync(), _ => !IsBusy && !_disposed);
        RenameCommand = new AsyncRelayCommand(_ => RenameAsync(), _ => !IsBusy && Selected is not null && !_disposed);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => !IsBusy && Selected is not null && !_disposed);
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
    }

    /// <summary>固定集合，目录切换完成后才替换条目。</summary>
    public ObservableCollection<SshFileEntry> Entries { get; } = [];
    /// <summary>本机名称筛选不发送命令。</summary>
    public ICollectionView VisibleEntries { get; }
    /// <summary>保留最近一百个传输结果。</summary>
    public ObservableCollection<SshTransferItem> Transfers { get; } = [];
    /// <summary>远端 POSIX 路径。</summary>
    public string Path { get; set => SetProperty(ref field, value); } = ".";
    /// <summary>目录内名称筛选。</summary>
    public string Filter { get; set { if (SetProperty(ref field, value)) VisibleEntries.Refresh(); } } = "";
    /// <summary>当前条目。</summary>
    public SshFileEntry? Selected { get; set { if (SetProperty(ref field, value)) RefreshCommands(); } }
    /// <summary>文件操作与传输不重叠。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) RefreshCommands(); } }
    /// <summary>安全错误，不回显 SDK 文案。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>刷新目录。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>返回上级。</summary>
    public AsyncRelayCommand ParentCommand { get; }
    /// <summary>上传单个文件。</summary>
    public AsyncRelayCommand UploadCommand { get; }
    /// <summary>下载所选文件。</summary>
    public AsyncRelayCommand DownloadCommand { get; }
    /// <summary>创建目录。</summary>
    public AsyncRelayCommand CreateDirectoryCommand { get; }
    /// <summary>重命名条目。</summary>
    public AsyncRelayCommand RenameCommand { get; }
    /// <summary>确认后删除条目。</summary>
    public AsyncRelayCommand DeleteCommand { get; }
    /// <summary>取消进行中的文件操作。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>双击目录才进入；文件不会自动下载或执行。</summary>
    public Task OpenAsync(SshFileEntry entry)
    {
        if (IsBusy || !entry.IsDirectory) return Task.CompletedTask;
        Path = entry.Path; return RunAsync(LoadAsync);
    }

    private ISshWorkspaceConnection Current => _connection() ?? throw Text.Exception("SshWorkspaceUnavailable");
    private async Task LoadAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Path) || Path.Any(char.IsControl)) throw Text.Exception("SshFileNameInvalid");
        string path = Path;
        var entries = await Current.ListDirectoryAsync(path, token);
        token.ThrowIfCancellationRequested();
        if (path != Path) return;
        Entries.Clear(); foreach (var item in entries) Entries.Add(item);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _disposed) return;
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = "";
        try { await action(pending.Token); }
        catch (OperationCanceledException) { if (!_disposed) Error = Text.Get("SshTransferCanceled"); }
        catch (Exception error) { if (!_disposed) Error = Text.Error(error, "SshFilesFailed"); }
        finally { _pending = null; IsBusy = false; }
    }

    private Task UploadAsync()
    {
        long revision = _revision;
        string? source = _dialogs.ChooseUpload();
        if (source is null || revision != _revision) return Task.CompletedTask;
        return RunAsync(async token =>
        {
            string name = System.IO.Path.GetFileName(source), target = SshRemotePath.CombineName(Path, name);
            await TransferAsync(name, true, async progress =>
            {
                await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                await Current.UploadAsync(file, target, progress, token);
            });
            await LoadAsync(token);
        });
    }

    private Task DownloadAsync()
    {
        long revision = _revision;
        var entry = Selected;
        if (entry is null || entry.IsDirectory) return Task.CompletedTask;
        string? target = _dialogs.ChooseDownload(entry.Name);
        if (target is null || revision != _revision) return Task.CompletedTask;
        return RunAsync(async token =>
        {
            string temporary = target + ".hermod-" + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await TransferAsync(entry.Name, false, async progress =>
                {
                    await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        await Current.DownloadAsync(entry.Path, file, progress, token);
                        await file.FlushAsync(token);
                    }
                    token.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
                });
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        });
    }

    private async Task TransferAsync(string name, bool upload, Func<IProgress<long>, Task> operation)
    {
        var item = new SshTransferItem(name, upload); Transfers.Add(item);
        while (Transfers.Count > 100) Transfers.RemoveAt(0);
        try { await operation(new Progress<long>(count => { if (!_disposed) item.Bytes = count; })); item.StatusKey = "SshTransferCompleted"; }
        catch (OperationCanceledException) { item.StatusKey = "SshTransferCanceled"; throw; }
        catch { item.StatusKey = "SshTransferFailed"; throw; }
    }

    private Task CreateAsync()
    {
        long revision = _revision;
        string? name = _dialogs.RequestName(Text.Get("SshFilesNewFolder"), "");
        return name is null || revision != _revision ? Task.CompletedTask : RunAsync(async token => { await Current.CreateDirectoryAsync(SshRemotePath.CombineName(Path, name), token); await LoadAsync(token); });
    }

    private Task RenameAsync()
    {
        long revision = _revision;
        var entry = Selected; if (entry is null) return Task.CompletedTask;
        string? name = _dialogs.RequestName(Text.Get("SshFilesRename"), entry.Name);
        return name is null || revision != _revision ? Task.CompletedTask : RunAsync(async token =>
        { await Current.RenameAsync(entry.Path, SshRemotePath.CombineName(SshRemotePath.Parent(entry.Path), name), token); await LoadAsync(token); });
    }

    private async Task DeleteAsync()
    {
        long revision = _revision;
        var entry = Selected; if (entry is null) return;
        if (!await _dialogs.ConfirmActionAsync(Text.Get("SshFilesDelete"), Text.Get("SshFilesDeleteHint"), entry.Path)) return;
        if (revision != _revision) return;
        await RunAsync(async token => { await Current.DeleteAsync(entry.Path, entry.IsDirectory, token); await LoadAsync(token); });
    }

    private void RefreshCommands()
    {
        foreach (var command in new[] { RefreshCommand, ParentCommand, UploadCommand, DownloadCommand, CreateDirectoryCommand, RenameCommand, DeleteCommand }) command?.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>重连或断线停止正在使用旧传输的任务。</summary>
    public void Cancel() { _revision++; _pending?.Cancel(); }
    /// <inheritdoc />
    public void Dispose() { _disposed = true; Cancel(); }
}
