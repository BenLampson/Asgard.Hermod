using Asgard.Hermod.Redis.UI.Services;

namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>每个数据库和原始键拥有独立格式、分页、草稿和加载生命周期。</summary>
public sealed partial class RedisKeyTabViewModel : ObservableObject, IDisposable
{
    private readonly IRedisSession _session;
    private readonly IRedisWorkspaceDialogs _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private RedisSnapshot _snapshot = RedisSnapshot.Missing;
    private RedisSnapshot? _baseline;
    private bool _rendering;
    private int _readGeneration;
    private bool _permissionDenied;
    private bool _unknownWrite;
    private string _format = "text";
    private bool _formatInitialized;

    /// <summary>页签顺序由工作页管理，不连接外层 AppShell 页签。</summary>
    public RedisKeyTabViewModel(IRedisSession session, int database, RedisKeyId key, IRedisWorkspaceDialogs dialogs, InstanceItemViewModel? instance = null)
    {
        _session = session; Database = database; Key = key; _dialogs = dialogs;
        Instance = instance;
        EditCommand = new AsyncRelayCommand(_ => EditAsync(), _ => CanEdit);
        OpenEditorCommand = new AsyncRelayCommand(_ => _dialogs.EditAsync(this), _ => CanEdit && !IsBusy);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => CanSave);
        DiscardCommand = new RelayCommand(_ => Discard(), _ => IsEditing && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !IsBusy);
        NextPageCommand = new AsyncRelayCommand(_ => ReadAsync(true), _ => _snapshot.HasMore && !IsEditing && !IsBusy);
        AddEntryCommand = new RelayCommand(_ => AddEntry(false), _ => IsEditing && !IsString && !IsBusy);
        AddHeadCommand = new RelayCommand(_ => AddEntry(true), _ => IsEditing && IsList && !IsBusy);
        RemoveEntryCommand = new RelayCommand(row => RemoveEntry(row as RedisEntryViewModel), _ => IsEditing && !IsBusy);
        AppendCommand = new RelayCommand(_ => BeginAppend(), _ => IsStream && !IsEditing && !IsBusy && !_permissionDenied && !_unknownWrite);
        OpenAppendCommand = new AsyncRelayCommand(_ => _dialogs.EditAsync(this, true), _ => AppendCommand.CanExecute(null));
        ExpiryCommand = new AsyncRelayCommand(_ => ChangeExpiryAsync(), _ => !IsBusy && !_permissionDenied && !_unknownWrite && Type != "none");
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => !IsBusy && !_permissionDenied && !_unknownWrite && !IsDirty && Type != "none");
    }

    /// <summary>原始键字节构成身份，标题截断不会改变身份。</summary>
    public RedisKeyId Key { get; }
    /// <summary>当前键所属数据库。</summary>
    public int Database { get; }
    /// <summary>当前操作的实例身份来自工作页，与树的选中项无关。</summary>
    public InstanceItemViewModel? Instance { get; }
    /// <summary>编辑和删除始终使用此页签的数据库，而非当前浏览数据库。</summary>
    public string TargetLabel => Text.Format("RedisWriteTarget", Instance?.Name ?? Text.Get("InstanceUnknown"),
        Instance?.EnvironmentLabel ?? Text.Get("EnvironmentNotSet"), Database);
    /// <summary>完整键名用于悬浮、键盘焦点提示和可访问名称。</summary>
    public string FullName => Key.DisplayName;
    /// <summary>原始标题由公共控件按实际宽度省略，不影响键身份。</summary>
    public string Header => FullName;
    /// <summary>页签图标由真实类型决定。</summary>
    public string Icon => Type is "hash" or "list" or "set" or "zset" ? "table-2" : "file";
    /// <summary>选中态只改变展示，不提交草稿。</summary>
    public bool IsActive { get; set => SetProperty(ref field, value); }
    /// <summary>同名键在不同数据库分别打开。</summary>
    public string Identity => Database.ToString(CultureInfo.InvariantCulture) + ":" + Key.Identity;
    /// <summary>菜单显示完整键与数据库，避免截断标题相同造成混淆。</summary>
    public string MenuLabel => Text.Format("RedisDatabaseLabel", Database) + " · " + FullName;
    /// <summary>真实 Redis 类型不翻译。</summary>
    public string Type => _snapshot.Type;
    /// <summary>字符串使用代码编辑器。</summary>
    public bool IsString => Type == "string";
    /// <summary>集合使用独立行编辑。</summary>
    public bool IsCollection => Type is "hash" or "list" or "set" or "zset" or "stream";
    /// <summary>列表索引不能当成员名称编辑。</summary>
    public bool IsList => Type == "list";
    /// <summary>Stream 原记录不支持修改。</summary>
    public bool IsStream => Type == "stream";
    /// <summary>集合类型决定字段、值、分数与记录 ID 的列。</summary>
    public bool HasValue => Type is "hash" or "list" or "stream";
    /// <summary>有序集合才显示分数。</summary>
    public bool HasScore => Type == "zset";
    /// <summary>数据条目数量或字符串字节数。</summary>
    public long Length => _snapshot.Length;
    /// <summary>TTL 是读取时剩余时间，不被当成创建时间。</summary>
    public string Ttl => _snapshot.TtlMilliseconds == -1 ? Text.Get("RedisPermanent") : _snapshot.TtlMilliseconds == -2 ? Text.Get("RedisMissing")
        : Text.Format("RedisRemaining", Math.Max(0, _snapshot.TtlMilliseconds / 1000));
    /// <summary>键缺失明确提示。</summary>
    public bool IsMissing => Type == "none";
    /// <summary>值和元数据是内层内容分区，不是键页签。</summary>
    public IReadOnlyList<InstanceFieldChoice> Sections { get; } = [new("value", Text.Get("RedisValue")), new("metadata", Text.Get("RedisMetadata"))];
    /// <summary>当前内容分区。</summary>
    public string Section { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(ShowValue)); OnPropertyChanged(nameof(ShowMetadata)); } } } = "value";
    /// <summary>值分区。</summary>
    public bool ShowValue => Section == "value";
    /// <summary>元数据分区。</summary>
    public bool ShowMetadata => Section == "metadata";
    /// <summary>有效 UTF-8 才提供文本和 JSON。集合字段只用文本或 Base64。</summary>
    public IReadOnlyList<InstanceFieldChoice> Formats { get; private set; } = [];
    /// <summary>格式切换先解析当前草稿，失败保留当前编辑文本。</summary>
    public string Format { get => _format; set => ChangeFormat(value); }
    /// <summary>当前字符串的独立编辑缓冲。</summary>
    public string EditorText { get; set { if (SetProperty(ref field, value) && !_rendering && IsEditing) MarkDirty(); } } = "";
    /// <summary>每个页签独立的集合行。</summary>
    public ObservableCollection<RedisEntryViewModel> Entries { get; } = [];
    /// <summary>只在完整基线已取得时开始编辑。</summary>
    public bool IsEditing { get; private set; }
    /// <summary>切换页签不会提交或清除草稿。</summary>
    public bool IsDirty { get; private set; }
    /// <summary>提交和读取期间禁止重复写入。</summary>
    public bool IsBusy { get; private set; }
    /// <summary>预览默认只读。</summary>
    public bool IsReadOnly => !IsEditing || IsBusy;
    /// <summary>完整预览才允许获取安全编辑基线。</summary>
    public bool CanEdit => Type is "string" or "hash" or "list" or "set" or "zset" && !IsEditing && !IsBusy && !_permissionDenied && !_unknownWrite;
    /// <summary>结果未知时必须先核对，不自动重试。</summary>
    public bool CanSave => IsEditing && IsDirty && !IsBusy && !_permissionDenied && !_unknownWrite;
    /// <summary>安全反馈不包含原始错误或值。</summary>
    public string Error { get; private set; } = "";
    /// <summary>错误为空不保留占位行。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>没有下一页时不显示无效分页动作。</summary>
    public bool HasMore => _snapshot.HasMore && !IsEditing;
    /// <summary>列表提供明确的尾部添加入口。</summary>
    public string AddEntryLabel => Text.Get(IsList ? "RedisAppendTail" : "RedisAddEntry");
    /// <summary>仅集合编辑显示行操作。</summary>
    public bool CanAddRows => IsCollection && IsEditing;
    /// <summary>仅列表编辑显示头部添加。</summary>
    public bool CanAddHead => IsList && IsEditing;
    /// <summary>草稿和只读状态供公共徽标显示。</summary>
    public string StateLabel => Text.Get(IsBusy ? "RedisLoading" : IsDirty ? "RedisDraft" : IsEditing ? "RedisEditValue" : "RedisReadOnly");
    /// <summary>预览底栏按完整句式说明字节数和编辑状态。</summary>
    public string PreviewInfo => Text.Format("RedisPreviewInfo", IsString ? Length : _snapshot.Entries.Sum(entry => entry.Name.Length + entry.Value.Length),
        Text.Get(IsDirty ? "RedisDraft" : IsEditing ? "RedisEditValue" : "RedisReadOnly"));
    /// <summary>展示当前内容的副本，用于用户主动复制。</summary>
    public string GetDisplayedText() => IsString ? EditorText : string.Join(Environment.NewLine, Entries.Select(entry => entry.RecordId + "\t" + entry.Name + "\t" + entry.Value + "\t" + entry.Score));
    /// <summary>控件错误也经过共享安全资源处理。</summary>
    public void ReportUiError(Exception error) => SetError(error);
    /// <summary>写入成功删除后，工作页只关闭这一个页签。</summary>
    public event Action<RedisKeyTabViewModel>? Deleted;
    /// <summary>命令复用公共异步与同步命令实现。</summary>
    public AsyncRelayCommand EditCommand { get; }
    /// <summary>内容页只打开编辑窗口，不切换成内联可写控件。</summary>
    public AsyncRelayCommand OpenEditorCommand { get; }
    /// <summary>Stream 追加也在独立窗口内收集字段。</summary>
    public AsyncRelayCommand OpenAppendCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public RelayCommand AddEntryCommand { get; }
    public RelayCommand AddHeadCommand { get; }
    public RelayCommand RemoveEntryCommand { get; }
    public RelayCommand AppendCommand { get; }
    public AsyncRelayCommand ExpiryCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    private void MarkDirty() { if (_rendering) return; IsDirty = true; RefreshState(); }
    private RedisSnapshot Draft() => _snapshot with
    {
        IsComplete = true,
        Entries = IsString ? [new([], RedisValueCodec.Decode(EditorText, Format))]
        : Entries.Select(entry => entry.ToEntry(Format)).ToArray()
    };
    private void Render(RedisSnapshot snapshot, bool editing = false)
    {
        _rendering = true;
        try
        {
            _snapshot = snapshot;
            if (snapshot.IsTruncated) Error = Text.Get("RedisValueTooLarge");
            bool utf8 = snapshot.Entries.All(entry => RedisValueCodec.IsUtf8(entry.Name) && RedisValueCodec.IsUtf8(entry.Value));
            if (!_formatInitialized && IsString && utf8)
            {
                _formatInitialized = true;
                try { _ = RedisValueCodec.Encode(snapshot.Entries.FirstOrDefault()?.Value ?? [], "json", true); _format = "json"; }
                catch { _format = "text"; }
            }
            Formats = utf8 ? IsString ? [new("text", Text.Get("RedisText")), new("json", Text.Get("RedisJson")), new("hex", Text.Get("RedisHex")), new("base64", Text.Get("RedisBase64"))]
                : [new("text", Text.Get("RedisText")), new("base64", Text.Get("RedisBase64"))]
                : IsString ? [new("hex", Text.Get("RedisHex")), new("base64", Text.Get("RedisBase64"))] : [new("base64", Text.Get("RedisBase64"))];
            if (!Formats.Any(format => format.Id == _format)) _format = utf8 ? "text" : "base64";
            if (IsString)
            {
                try { EditorText = RedisValueCodec.Encode(snapshot.Entries.FirstOrDefault()?.Value ?? [], Format, !editing); }
                catch { _format = utf8 ? "text" : "base64"; EditorText = RedisValueCodec.Encode(snapshot.Entries.FirstOrDefault()?.Value ?? [], Format); }
            }
            Entries.Clear(); foreach (var entry in snapshot.Entries) Entries.Add(new(entry, Format is "text" or "base64" ? Format : "text", MarkDirty));
        }
        finally { _rendering = false; RefreshState(); }
    }
    private void ChangeFormat(string value)
    {
        if (_rendering || value == _format || !Formats.Any(format => format.Id == value)) return;
        try
        {
            var source = IsEditing ? Draft() : _snapshot;
            _format = value; Render(source, IsEditing);
        }
        catch (Exception error) { SetError(error); OnPropertyChanged(nameof(Format)); }
    }
    private void SetError(Exception error)
    {
        Error = Text.Error(error, "RedisCommandFailed");
        if (error is RedisException safe) { _permissionDenied |= safe.Code == "REDIS_PERMISSION"; _unknownWrite |= safe.Code == "REDIS_WRITE_UNKNOWN"; }
        RefreshState();
    }
    private void RefreshState()
    {
        foreach (string name in new[] { nameof(Type), nameof(Icon), nameof(PreviewInfo), nameof(IsString), nameof(IsCollection), nameof(IsList), nameof(IsStream), nameof(HasValue), nameof(HasScore), nameof(Length),
            nameof(Ttl), nameof(IsMissing), nameof(Formats), nameof(Format), nameof(IsEditing), nameof(IsDirty), nameof(IsBusy), nameof(IsReadOnly), nameof(CanEdit), nameof(CanSave), nameof(Error), nameof(StateLabel),
            nameof(HasError), nameof(HasMore), nameof(AddEntryLabel), nameof(CanAddRows), nameof(CanAddHead) }) OnPropertyChanged(name);
        EditCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); DiscardCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
        OpenEditorCommand.NotifyCanExecuteChanged(); OpenAppendCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged(); AddEntryCommand.NotifyCanExecuteChanged(); AddHeadCommand.NotifyCanExecuteChanged(); RemoveEntryCommand.NotifyCanExecuteChanged();
        AppendCommand.NotifyCanExecuteChanged(); ExpiryCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
    }
    /// <inheritdoc />
    public void Dispose() { _readGeneration++; _lifetime.Cancel(); _lifetime.Dispose(); }
}
