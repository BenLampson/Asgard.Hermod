namespace Asgard.Hermod.UI.ViewModels;

/// <summary>先读取完整子树与关联影响，只有用户执行删除命令才调用持久化删除。</summary>
public sealed class TagDeletionViewModel : ObservableObject, IDisposable
{
    private readonly ITagManagementService _service;
    private readonly ManagedTag _tag;
    private readonly CancellationTokenSource _lifetime = new();
    private TagDeletionPreview? _preview;
    private string _error = "";
    private bool _isBusy;
    private bool _isDisposed;

    /// <summary>绑定待删除标签，系统环境无法通过此状态提交删除。</summary>
    public TagDeletionViewModel(ITagManagementService service, ManagedTag tag)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(tag);
        _service = service;
        _tag = tag;
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => CanDelete);
        RetryCommand = new AsyncRelayCommand(_ => InitializeAsync(), _ => !IsBusy && Preview is null);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false), _ => !IsBusy);
    }

    /// <summary>删除成功或取消时请求结束确认窗口。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>只有实际删除完成才为真。</summary>
    public bool WasDeleted { get; private set; }
    /// <summary>后端计算的完整子树和去重后的受影响实例数量。</summary>
    public TagDeletionPreview? Preview
    {
        get => _preview;
        private set
        {
            if (!SetProperty(ref _preview, value)) return;
            OnPropertyChanged(nameof(Tags));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Impact));
            OnPropertyChanged(nameof(DeleteLabel));
            RefreshCommands();
        }
    }
    /// <summary>显示所有将删除的路径，包含根标签本身。</summary>
    public IReadOnlyList<ManagedTag> Tags => Preview?.Tags ?? [];
    /// <summary>叶子和含子级的标签使用同一确认窗口。</summary>
    public string Title => Tags.Count > 1 ? Text.Format("DeleteAndItsChildTags", _tag.Name) : Text.Format("Delete2", _tag.Name);
    /// <summary>影响列表上方的真实数量。</summary>
    public string Summary => Preview is null ? Text.Get("ReadingDeletionImpact") : Text.Format("TheFollowingCustomTagsWillBeDeleted", Tags.Count);
    /// <summary>说明删除仅影响关联，实例和连接信息继续保留。</summary>
    public string Impact => Preview is null ? ""
        : Text.Format("RelatedTagLinksWillAlsoBeRemovedFromInstancesInstancesAndConnectionInformationWillRem", Preview.AffectedInstanceCount);
    /// <summary>确认按钮明确包含标签数量。</summary>
    public string DeleteLabel => IsBusy ? Text.Get("Processing") : Preview is null ? Text.Get("DeleteTag") : Text.Format("DeleteTags", Tags.Count);
    /// <summary>失败消息与影响列表一起保留，允许再次执行。</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>读取和提交期间关闭操作按钮，防止重复删除。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(DeleteLabel));
            RefreshCommands();
        }
    }
    /// <summary>必须先有删除预览，并且整棵树均为自定义标签。</summary>
    public bool CanDelete => !_isDisposed && !IsBusy && !_tag.IsBuiltIn && Preview is not null
        && Tags.Count > 0 && Tags.All(tag => !tag.IsBuiltIn);
    /// <summary>显式确认后执行真实删除。</summary>
    public AsyncRelayCommand DeleteCommand { get; }
    /// <summary>删除影响读取失败时重试。</summary>
    public AsyncRelayCommand RetryCommand { get; }
    /// <summary>取消确认。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>仅读取影响，不会在初始化过程中删除任何数据。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed || IsBusy || Preview is not null) return;
        if (_tag.IsBuiltIn) { Error = Text.Get("BuiltInEnvironmentTagsCannotBeDeleted"); return; }
        IsBusy = true;
        Error = "";
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var preview = await _service.PreviewDeleteAsync(_tag.Id, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            Preview = preview;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { Error = Text.Error(exception); }
        finally { IsBusy = false; }
    }

    /// <summary>失败后保留影响预览，只有成功才结束窗口。</summary>
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDelete) return;
        IsBusy = true;
        Error = "";
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await _service.DeleteAsync(_tag.Id, operation.Token);
            WasDeleted = true;
            if (!_isDisposed) CloseRequested?.Invoke(this, true);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { Error = Text.Error(exception); }
        finally { IsBusy = false; }
    }

    /// <summary>读取和执行状态变化时同步确认按钮。</summary>
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanDelete));
        DeleteCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <summary>窗口结束后取消读取并释放资源。</summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        RefreshCommands();
    }
}
