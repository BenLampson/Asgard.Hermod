namespace Asgard.Hermod.UI.ViewModels;

/// <summary>轻量名称或标签草稿；提交失败保留输入，取消不调用持久化操作。</summary>
public sealed class MetadataEditorViewModel : ObservableObject, IDisposable
{
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task> _save;
    private readonly bool _isTagName;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    /// <summary>调用方提供持久化动作，公共窗口不接触连接或凭据。</summary>
    public MetadataEditorViewModel(string title, string nameLabel, string name, int maxLength,
        Func<string, IReadOnlyList<string>, CancellationToken, Task> save, bool isTagName = false,
        bool isTagEditing = false, IReadOnlyList<ManagedTag>? tags = null, IReadOnlyList<string>? selectedIds = null, bool editNameAndTags = false)
    {
        Title = title; NameLabel = nameLabel; Name = name; MaxLength = maxLength; _save = save; _isTagName = isTagName;
        IsTagEditing = isTagEditing;
        EditNameAndTags = editNameAndTags;
        Tags = new(tags, selectedIds); Tags.SetEditable(isTagEditing); Tags.PropertyChanged += OnTagsChanged;
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => CanSave);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false), _ => !IsBusy);
    }

    /// <summary>名称与标签均使用独立窗口标题。</summary>
    public string Title { get; }
    /// <summary>输入标签来自共享资源。</summary>
    public string NameLabel { get; }
    /// <summary>实例和标签分别遵循领域名称长度。</summary>
    public int MaxLength { get; }
    /// <summary>标签编辑不展示名称输入。</summary>
    public bool IsTagEditing { get; }
    /// <summary>实例重命名同时编辑名称和标签，在一次事务中提交。</summary>
    public bool EditNameAndTags { get; }
    /// <summary>重命名只展示名称。</summary>
    public bool IsNameEditing => !IsTagEditing || EditNameAndTags;
    /// <summary>提交期间冻结输入。</summary>
    public bool IsEditable => !_disposed && !IsBusy;
    /// <summary>独立弹出标签草稿复用共享选择规则。</summary>
    public InstanceTagSelectionViewModel Tags { get; }
    /// <summary>名称输入在失败后原样保留。</summary>
    public string Name { get; set { if (SetProperty(ref field, value ?? "")) Refresh(); } } = "";
    /// <summary>实例和标签使用各自已有的名称校验文案。</summary>
    public string NameError => !IsNameEditing ? "" : _isTagName
        ? string.IsNullOrWhiteSpace(Name) ? Text.Get("EnterATagName")
            : Name.Trim().Length > 80 || Name.Contains('/') || Name.Any(char.IsControl)
                ? Text.Get("TagNamesMustContain180CharactersAndNoSlashesOrControlCharacters") : ""
        : InstanceFieldValidation.GetError(new("name", NameLabel, MaxLength: MaxLength), Name);
    /// <summary>读取或保存错误使用安全本地化消息。</summary>
    public string Error { get; private set; } = "";
    /// <summary>保存期间防止关闭、重复提交和更改草稿。</summary>
    public bool IsBusy { get; private set; }
    /// <summary>只有真正保存成功才关闭并返回结果。</summary>
    public bool WasSaved { get; private set; }
    /// <summary>未确认标签浮层阻止回车穿透保存。</summary>
    public bool CanSave => IsEditable && NameError.Length == 0 && !Tags.HasOpenPicker;
    /// <summary>根据实际保存状态提供文案。</summary>
    public string SaveLabel => Text.Get(IsBusy ? "Saving" : "SaveChanges");
    /// <summary>执行调用方提供的元数据事务。</summary>
    public AsyncRelayCommand SaveCommand { get; }
    /// <summary>取消不落库。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>只有保存成功或明确取消才请求宿主关闭。</summary>
    public event EventHandler<bool>? CloseRequested;

    private async Task SaveAsync()
    {
        if (!CanSave) return;
        IsBusy = true; Error = ""; Tags.SetEditable(false); Refresh();
        try
        {
            await _save(Name.Trim(), Tags.SelectedIds, _lifetime.Token);
            WasSaved = true;
            CloseRequested?.Invoke(this, true);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error) { if (!_disposed) Error = Text.Error(error); }
        finally { IsBusy = false; Tags.SetEditable(IsTagEditing && !_disposed); Refresh(); }
    }

    private void OnTagsChanged(object? sender, PropertyChangedEventArgs args) => Refresh();
    private void Refresh()
    {
        foreach (string name in new[] { nameof(IsEditable), nameof(NameError), nameof(Error), nameof(IsBusy), nameof(CanSave), nameof(SaveLabel) }) OnPropertyChanged(name);
        SaveCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged();
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Tags.PropertyChanged -= OnTagsChanged; Tags.SetEditable(false); _lifetime.Cancel(); _lifetime.Dispose(); Refresh();
    }
}
