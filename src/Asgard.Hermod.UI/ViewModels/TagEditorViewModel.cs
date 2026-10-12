namespace Asgard.Hermod.UI.ViewModels;

/// <summary>保存可重试的标签草稿，父级与 HEX 预校验独立于持久化服务。</summary>
public sealed class TagEditorViewModel : ObservableObject, IDisposable
{
    private readonly ITagManagementService _service;
    private readonly ManagedTag? _original;
    private readonly CancellationTokenSource _lifetime = new();
    private string _name;
    private string? _parentId;
    private TagParentOption? _selectedParent;
    private string _colorText;
    private string _previewColor = "#3B82F6";
    private string _colorError = "";
    private string _error = "";
    private bool _isBusy;
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>编辑时保留原标识，新建子级时只预填父级。</summary>
    public TagEditorViewModel(ITagManagementService service, ManagedTag? tag = null, string? parentId = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _original = tag;
        _name = tag?.Name ?? "";
        _parentId = tag?.ParentId ?? parentId;
        _colorText = tag?.Color ?? "#3B82F6";
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => CanSave);
        RetryCommand = new AsyncRelayCommand(_ => InitializeAsync(), _ => !IsBusy && !IsInitialized);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false), _ => !IsBusy);
        SelectColorCommand = new RelayCommand(parameter =>
        {
            if (parameter is string color)
                ColorText = color;
        }, _ => !IsBusy);
        UpdateColorPreview();
    }

    /// <summary>成功保存或取消时请求宿主结束窗口。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>保存成功的真实服务快照，取消和失败时为空。</summary>
    public ManagedTag? Result { get; private set; }
    /// <summary>显示新建或编辑标题。</summary>
    public string Title => _original is null ? Text.Get("NewTag") : Text.Get("EditTag");
    /// <summary>设计稿中的主操作名称。</summary>
    public string SaveLabel => IsBusy ? Text.Get("Saving") : _original is null ? Text.Get("CreateTag") : Text.Get("SaveChanges2");
    /// <summary>保存说明保持关联规则可见。</summary>
    public string SaveHint => _original is null
        ? Text.Get("CreateCustomTagsWithoutChangingBuiltInEnvironmentTagsSavedTagsAppearImmediatelyInTagM")
        : Text.Format("ThisTagIsReferencedByInstancesSavingUpdatesItsNamePathAndColorWithoutChangingInstance", _original.InstanceCount);
    /// <summary>可选父级列表，始终含根级。</summary>
    public ObservableCollection<TagParentOption> ParentOptions { get; } = [];
    /// <summary>设计稿的八种快捷颜色。</summary>
    public IReadOnlyList<TagColorSwatchViewModel> Swatches { get; } =
        new[] { "#3B82F6", "#8B5CF6", "#EC4899", "#EF4444", "#F59E0B", "#22C55E", "#14B8A6", "#64748B" }
            .Select(color => new TagColorSwatchViewModel(color)).ToArray();
    /// <summary>名称草稿，不因保存失败而丢弃。</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value)) return;
            Error = "";
            OnPropertyChanged(nameof(NameError));
            OnPropertyChanged(nameof(PreviewPath));
            RefreshCommands();
        }
    }
    /// <summary>提交标识和父级候选必须一致，避免从外部绑定注入不合法父级。</summary>
    public string? ParentId
    {
        get => _parentId;
        set
        {
            if (!SetProperty(ref _parentId, value)) return;
            Error = "";
            _selectedParent = ParentOptions.FirstOrDefault(option => option.Id == value);
            OnPropertyChanged(nameof(SelectedParent));
            OnPropertyChanged(nameof(ParentError));
            OnPropertyChanged(nameof(PreviewPath));
            RefreshCommands();
        }
    }
    /// <summary>供选择控件绑定的父级对象。</summary>
    public TagParentOption? SelectedParent
    {
        get => _selectedParent;
        set
        {
            if (!SetProperty(ref _selectedParent, value)) return;
            ParentId = value?.Id;
            OnPropertyChanged(nameof(PreviewPath));
        }
    }
    /// <summary>保留原始颜色输入，短 HEX 的等价颜色显示在独立预览里。</summary>
    public string ColorText
    {
        get => _colorText;
        set
        {
            if (!SetProperty(ref _colorText, value)) return;
            Error = "";
            UpdateColorPreview();
        }
    }
    /// <summary>最后一次有效输入的规范颜色，错误输入不改变它。</summary>
    public string PreviewColor { get => _previewColor; private set => SetProperty(ref _previewColor, value); }
    /// <summary>根据真实父路径和名称草稿显示将保存的路径。</summary>
    public string PreviewPath => string.IsNullOrWhiteSpace(SelectedParent?.Id)
        ? Name.Trim() : $"{SelectedParent.DisplayName} / {Name.Trim()}";
    /// <summary>名称长度与路径字符规则预先反馈，同级唯一由持久化服务校验。</summary>
    public string NameError => string.IsNullOrWhiteSpace(Name) ? Text.Get("EnterATagName")
        : Name.Trim().Length > 80 || Name.Contains('/') || Name.Any(char.IsControl)
            ? Text.Get("TagNamesMustContain180CharactersAndNoSlashesOrControlCharacters") : "";
    /// <summary>父级必须来自过滤后的合法候选。</summary>
    public string ParentError => IsInitialized && !ParentOptions.Any(option => option.Id == ParentId)
        ? Text.Get("SelectTheRootOrAnotherAvailableCustomTag") : "";
    /// <summary>非法 HEX 只禁用保存，不清空上次有效预览。</summary>
    public string ColorError { get => _colorError; private set => SetProperty(ref _colorError, value); }
    /// <summary>读取和保存失败的服务消息。</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>读取或保存期间禁止再次提交与关闭。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(SaveLabel));
            RefreshCommands();
        }
    }
    /// <summary>字段在后台操作期间保持只读。</summary>
    public bool IsEditable => !IsBusy;
    /// <summary>父级来源完成读取后才允许提交。</summary>
    public bool IsInitialized
    {
        get => _isInitialized;
        private set { if (SetProperty(ref _isInitialized, value)) RefreshCommands(); }
    }
    /// <summary>内置环境标签没有编辑入口，也不能绕过入口提交。</summary>
    public bool CanSave => !_isDisposed && IsInitialized && !IsBusy && _original?.IsBuiltIn != true
        && NameError.Length == 0 && ParentError.Length == 0 && ColorError.Length == 0;
    /// <summary>保存当前草稿。</summary>
    public AsyncRelayCommand SaveCommand { get; }
    /// <summary>首次读取失败时重新载入候选。</summary>
    public AsyncRelayCommand RetryCommand { get; }
    /// <summary>取消编辑。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>快捷颜色选择。</summary>
    public RelayCommand SelectColorCommand { get; }

    /// <summary>过滤自身、全部后代和系统环境，失败后仍保留草稿用于重试。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed || IsBusy || IsInitialized) return;
        IsBusy = true;
        Error = "";
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var tags = await _service.GetTagsAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            if (_original is not null)
            {
                excluded.Add(_original.Id);
                bool changed;
                do
                {
                    changed = false;
                    foreach (var candidate in tags)
                        if (candidate.ParentId is not null && excluded.Contains(candidate.ParentId))
                            changed |= excluded.Add(candidate.Id);
                } while (changed);
            }
            ParentOptions.Clear();
            ParentOptions.Add(new TagParentOption(null, Text.Get("Root")));
            foreach (var candidate in tags.Where(tag => !tag.IsBuiltIn && !excluded.Contains(tag.Id))
                         .OrderBy(tag => tag.Path, StringComparer.OrdinalIgnoreCase))
                ParentOptions.Add(new TagParentOption(candidate.Id, candidate.Path.Replace("/", " / ", StringComparison.Ordinal)));
            _selectedParent = ParentOptions.FirstOrDefault(option => option.Id == ParentId);
            OnPropertyChanged(nameof(SelectedParent));
            OnPropertyChanged(nameof(PreviewPath));
            IsInitialized = true;
            OnPropertyChanged(nameof(ParentError));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { Error = Text.Error(exception); }
        finally { IsBusy = false; }
    }

    /// <summary>以规范颜色提交，失败不关闭窗口，允许修正或再次保存。</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave) return;
        IsBusy = true;
        Error = "";
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            Result = await _service.SaveAsync(_original?.Id, new TagEditDraft(Name.Trim(), ParentId, PreviewColor), operation.Token);
            if (!_isDisposed)
                CloseRequested?.Invoke(this, true);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { Error = Text.Error(exception); }
        finally { IsBusy = false; }
    }

    /// <summary>仅在输入有效时更新预览与快捷颜色选中状态。</summary>
    private void UpdateColorPreview()
    {
        if (TagHexColor.TryNormalize(ColorText, out var color))
        {
            PreviewColor = color;
            ColorError = "";
        }
        else ColorError = Text.Get("EnterAColorInRGBOrRRGGBBFormat");
        foreach (var swatch in Swatches)
            swatch.IsSelected = ColorError.Length == 0 && swatch.Color == PreviewColor;
        RefreshCommands();
    }

    /// <summary>字段和后台状态变化时同步命令启用条件。</summary>
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        SelectColorCommand.NotifyCanExecuteChanged();
    }

    /// <summary>结束未完成的读取，防止已关闭窗口继续接收状态。</summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        RefreshCommands();
    }
}
