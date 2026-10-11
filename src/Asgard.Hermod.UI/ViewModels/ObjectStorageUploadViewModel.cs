namespace Asgard.Hermod.UI.ViewModels;

/// <summary>上传窗体管理待传队列与冲突策略，只返回选择结果，不执行远程写入。</summary>
public sealed class ObjectStorageUploadViewModel : ObjectStorageDialogViewModel
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ObjectStorageUploadItem>>> _mapPaths;
    private readonly CancellationTokenSource _lifetime = new();
    private string _operationError = "";

    /// <summary>映射器可替换，使单元测试与操作窗体不依赖系统文件选择窗口。</summary>
    public ObjectStorageUploadViewModel(ObjectStorageBucket bucket, string prefix,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ObjectStorageUploadItem>>>? mapPaths = null)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        ArgumentNullException.ThrowIfNull(prefix);
        Location = bucket.Name + (prefix.Length == 0 ? " /" : " / " + prefix.TrimEnd('/'));
        _mapPaths = mapPaths ?? ((paths, token) => ObjectStorageUploadPathMapper.MapAsync(paths, prefix, token));
        SelectFilesCommand = new RelayCommand(_ => SelectFilesRequested?.Invoke(this, EventArgs.Empty), _ => CanAddPaths);
        SelectFolderCommand = new RelayCommand(_ => SelectFolderRequested?.Invoke(this, EventArgs.Empty), _ => CanAddPaths);
        RemoveCommand = new RelayCommand(parameter =>
        {
            if (parameter is ObjectStorageUploadQueueItemViewModel item)
                Items.Remove(item);
        }, parameter => CanAddPaths && parameter is ObjectStorageUploadQueueItemViewModel item && Items.Contains(item));
        ConfirmCommand = new RelayCommand(_ => Confirm(), _ => CanConfirm);
        Items.CollectionChanged += (_, _) => RefreshQueue();
    }

    /// <summary>文件系统选择由 Windows 窗体服务处理。</summary>
    public event EventHandler? SelectFilesRequested;
    /// <summary>文件夹选择使用目的明确的系统 API。</summary>
    public event EventHandler? SelectFolderRequested;
    /// <summary>当前 Bucket 与目录前缀。</summary>
    public string Location { get; }
    /// <summary>可移除的已选队列。</summary>
    public ObservableCollection<ObjectStorageUploadQueueItemViewModel> Items { get; } = [];
    /// <summary>首次打开时默认询问，同名覆盖必须遵循用户最后确认的策略。</summary>
    public IReadOnlyList<ObjectStorageOverwriteChoice> OverwriteChoices { get; } =
        [new(ObjectStorageOverwritePolicy.Ask, "询问后再上传"), new(ObjectStorageOverwritePolicy.Skip, "跳过同名对象"),
         new(ObjectStorageOverwritePolicy.Overwrite, "覆盖同名对象")];
    /// <summary>用户明确确认的同名对象处理方式。</summary>
    public ObjectStorageOverwritePolicy OverwritePolicy
    {
        get;
        set { if (Enum.IsDefined(value)) SetProperty(ref field, value); }
    } = ObjectStorageOverwritePolicy.Ask;
    /// <summary>枚举目录时允许取消，但禁止重复选择与提交。</summary>
    public bool IsAdding { get; private set; }
    /// <summary>可以继续添加、移除本地文件。</summary>
    public bool CanAddPaths => !IsDisposed && !IsAdding;
    /// <summary>同一队列不能把两个不同来源写入同一个对象键。</summary>
    public bool HasKeyConflicts => Items.GroupBy(item => item.Key, StringComparer.Ordinal).Any(group => group.Count() > 1);
    /// <summary>至少选择一个文件，且目录枚举已经完成。</summary>
    public bool CanConfirm => CanAddPaths && Items.Count > 0 && !HasKeyConflicts;
    /// <summary>选择数量与大小只计算已选本地文件。</summary>
    public string CountText => $"已选 {Items.Count} 个文件";
    /// <summary>待上传队列的可读总大小。</summary>
    public string SizeText => ObjectStoragePresentation.Size(Items.Sum(item => item.Item.Size));
    /// <summary>文件系统异常不回显内部路径或异常堆栈。</summary>
    public string Error => _operationError.Length > 0 ? _operationError : HasKeyConflicts
        ? "有文件将上传到相同对象键，请移除重名项。" : "";
    /// <summary>只有用户点击开始上传才返回不可变的文件快照。</summary>
    public ObjectStorageUploadSelection? Result { get; private set; }
    /// <summary>选择文件入口。</summary>
    public RelayCommand SelectFilesCommand { get; }
    /// <summary>选择文件夹入口。</summary>
    public RelayCommand SelectFolderCommand { get; }
    /// <summary>从待传队列移除一项。</summary>
    public RelayCommand RemoveCommand { get; }
    /// <summary>确认选择，实际上传由工作区服务负责。</summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>系统选择与拖拽复用同一异步枚举入口，不打开或读取文件正文。</summary>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!CanAddPaths)
            return;
        IsAdding = true;
        _operationError = "";
        RefreshQueue();
        try
        {
            var additions = await _mapPaths(paths.ToArray(), _lifetime.Token);
            if (IsDisposed)
                return;
            foreach (var item in additions)
            {
                if (!Items.Any(existing => existing.Key == item.Key
                    && string.Equals(existing.Item.LocalPath, item.LocalPath, StringComparison.OrdinalIgnoreCase)))
                    Items.Add(new(item));
            }
        }
        catch (OperationCanceledException) when (IsDisposed) { }
        catch (Exception exception)
        {
            if (!IsDisposed)
                _operationError = exception is ObjectStorageException safe ? safe.Message
                    : "无法添加所选文件，请检查访问权限后重试。";
        }
        finally
        {
            IsAdding = false;
            RefreshQueue();
        }
    }

    /// <summary>系统选择失败时使用固定说明，不显示系统异常。</summary>
    public void ReportSelectionError()
    {
        if (IsDisposed)
            return;
        _operationError = "无法打开文件选择窗口，请稍后重试。";
        RefreshQueue();
    }

    /// <summary>窗口关闭后终止尚未完成的本地目录枚举。</summary>
    public override void Dispose()
    {
        if (IsDisposed)
            return;
        base.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        RefreshQueue();
    }

    /// <summary>确认只生成任务输入，不通过窗体直接修改远程对象。</summary>
    private void Confirm()
    {
        Result = new(Items.Select(item => item.Item).ToArray(), OverwritePolicy);
        Close(true);
    }

    /// <summary>队列修改、枚举与关闭均统一刷新页脚和命令状态。</summary>
    private void RefreshQueue()
    {
        foreach (string property in new[] { nameof(IsAdding), nameof(CanAddPaths), nameof(HasKeyConflicts),
            nameof(CanConfirm), nameof(CountText), nameof(SizeText), nameof(Error) })
            OnPropertyChanged(property);
        SelectFilesCommand.NotifyCanExecuteChanged();
        SelectFolderCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
