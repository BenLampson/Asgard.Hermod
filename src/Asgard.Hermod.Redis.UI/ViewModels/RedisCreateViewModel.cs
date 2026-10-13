namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>新增键的独立草稿，错误保留输入；未知写入结果禁止自动重放。</summary>
public sealed class RedisCreateViewModel : ObservableObject, IDisposable
{
    private readonly IRedisSession _session;
    private readonly CancellationTokenSource _lifetime;
    private string _format = "text";
    /// <summary>数据库及前缀在打开时固定，不受工作区后来切换影响。</summary>
    public RedisCreateViewModel(IRedisSession session, int database, string prefix = "", CancellationToken cancellationToken = default)
    {
        _session = session; Database = database; Key = prefix;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SubmitCommand = new AsyncRelayCommand(_ => SubmitAsync(), _ => CanSubmit);
        AddRowCommand = new RelayCommand(_ => AddRow(), _ => CanAddRow);
        RemoveRowCommand = new RelayCommand(row => RemoveRow(row as RedisEntryViewModel), _ => !IsBusy && Rows.Count > 1);
        Rows.Add(new(new([], [], "0"), "text", () => { }));
    }
    /// <summary>创建目标数据库，不随窗口其他状态改变。</summary>
    public int Database { get; }
    /// <summary>目标数据库的完整本地化句式。</summary>
    public string TargetLabel => Text.Format("RedisCreateTarget", Database);
    /// <summary>真实键名保持原样，不裁剪空白或解释 MATCH。</summary>
    public string Key { get; set => SetProperty(ref field, value); } = "";
    /// <summary>协议类型名称不翻译。</summary>
    public IReadOnlyList<string> Types { get; } = ["string", "hash", "list", "set", "zset", "stream"];
    /// <summary>切换类型只改变当前类型的编辑器，字符串缓冲及集合草稿保留。</summary>
    public string SelectedType
    {
        get; set
        {
            if (!Types.Contains(value) || !SetProperty(ref field, value)) return;
            if (!IsString && _format is "json" or "hex") _format = "text";
            Refresh();
        }
    } = "string";
    /// <summary>字符串提供四种无损格式，集合提供文本与 Base64。</summary>
    public IReadOnlyList<InstanceFieldChoice> Formats => IsString
        ? [new("text", Text.Get("RedisText")), new("json", Text.Get("RedisJson")), new("hex", Text.Get("RedisHex")), new("base64", Text.Get("RedisBase64"))]
        : [new("text", Text.Get("RedisText")), new("base64", Text.Get("RedisBase64"))];
    /// <summary>格式转换先解析所有输入，失败时不更改缓冲或当前格式。</summary>
    public string Format
    {
        get => _format;
        set
        {
            if (value == _format || !Formats.Any(choice => choice.Id == value) || IsBusy) return;
            try
            {
                if (IsString)
                {
                    byte[] bytes = EditorText.Length == 0 ? [] : RedisValueCodec.Decode(EditorText, _format);
                    string converted = RedisValueCodec.Encode(bytes, value);
                    if (value == "json" && converted.Length > 0) _ = RedisValueCodec.Decode(converted, value);
                    EditorText = converted;
                }
                else
                {
                    var converted = Rows.Select(row => ReadRow(row, _format)).Select(entry => new RedisEntryViewModel(entry, value, () => { })).ToArray();
                    Rows.Clear(); foreach (var row in converted) Rows.Add(row);
                }
                _format = value; Error = ""; Refresh();
            }
            catch (Exception error) { Error = Text.Error(error, "RedisInvalidFormat"); Refresh(); }
        }
    }
    /// <summary>字符串原始编辑内容，JSON 验证不自动重新格式化。</summary>
    public string EditorText { get; set => SetProperty(ref field, value); } = "";
    /// <summary>集合初始元素，至少保留一行。</summary>
    public ObservableCollection<RedisEntryViewModel> Rows { get; } = [];
    /// <summary>字符串编辑器独立于集合表格。</summary>
    public bool IsString => SelectedType == "string";
    /// <summary>其余类型使用初始元素表格。</summary>
    public bool IsCollection => !IsString;
    /// <summary>字段及成员列按协议类型显示。</summary>
    public bool HasName => SelectedType is "hash" or "set" or "zset" or "stream";
    /// <summary>集合值列适用于 Hash、List 和 Stream。</summary>
    public bool HasValue => SelectedType is "hash" or "list" or "stream";
    /// <summary>只有有序集合具有分数列。</summary>
    public bool HasScore => SelectedType == "zset";
    /// <summary>成员与字段显示对应列名，行为仍只比较稳定类型。</summary>
    public string NameLabel => Text.Get(SelectedType is "set" or "zset" ? "RedisMember" : "RedisField");
    /// <summary>输入行数受协议编辑上限约束。</summary>
    public bool CanAddRow => IsEditable && Rows.Count < RedisSnapshot.MaximumEntries;
    /// <summary>初始过期方式：永久或正整数秒。</summary>
    public IReadOnlyList<InstanceFieldChoice> ExpiryModes { get; } = [new("permanent", Text.Get("RedisPermanent")), new("seconds", Text.Get("RedisCreateExpiryAfter"))];
    /// <summary>使用稳定的过期方式标识，不比较翻译标签。</summary>
    public string ExpiryMode { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasExpiry)); } } = "permanent";
    /// <summary>定时过期时显示秒数输入。</summary>
    public bool HasExpiry => ExpiryMode == "seconds";
    /// <summary>秒数按 invariant 正整数解析。</summary>
    public string ExpirySeconds { get; set => SetProperty(ref field, value); } = "3600";
    /// <summary>向服务器提交期间锁定输入及关闭；不重复发送写入。</summary>
    public bool IsBusy { get; private set; }
    /// <summary>写入中、结果未知及创建成功后锁定草稿。</summary>
    public bool IsEditable => !IsBusy && !HasUnknownWrite && CreatedKey is null;
    /// <summary>没有服务端不确定结果时才允许提交。</summary>
    public bool CanSubmit => IsEditable;
    /// <summary>等待服务端确认期间阻止关闭窗口。</summary>
    public bool CanClose => !IsBusy;
    /// <summary>不确定结果只能检查目标键，不允许把修改后的输入重放成另一次创建。</summary>
    public bool HasUnknownWrite { get; private set; }
    /// <summary>未知结果或同名冲突可主动打开目标键核对。</summary>
    public bool CanInspect => HasUnknownWrite || HasConflict;
    /// <summary>同名键冲突不会删除原数据。</summary>
    public bool HasConflict { get; private set; }
    /// <summary>展示共享安全资源错误，不包含原始 SDK 信息。</summary>
    public string Error { get; private set; } = "";
    /// <summary>无错误时不占用消息行。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>键名字段的就地校验。</summary>
    public string KeyError { get; private set; } = "";
    /// <summary>初始 TTL 字段的就地校验。</summary>
    public string ExpiryError { get; private set; } = "";
    /// <summary>提交按钮显示当前进度。</summary>
    public string SubmitLabel => Text.Get(IsBusy ? "RedisCreatingKey" : "RedisCreateKey");
    /// <summary>成功创建后由窗口关闭并通知工作区打开这个键。</summary>
    public RedisKeyId? CreatedKey { get; private set; }
    /// <summary>冲突或未知结果时核对本次提交的原始键，而不是后来修改的输入。</summary>
    public RedisKeyId? SubmittedKey { get; private set; }
    /// <summary>创建确认成功后通知窗口退出并打开键页签。</summary>
    public event Action? Created;
    /// <summary>串行创建命令。</summary>
    public AsyncRelayCommand SubmitCommand { get; }
    /// <summary>新增一个集合输入行。</summary>
    public RelayCommand AddRowCommand { get; }
    /// <summary>删除选中行，始终保留至少一行。</summary>
    public RelayCommand RemoveRowCommand { get; }

    /// <summary>先验证所有类型、编码、重复项及大小，绝不为校验失败发送写入。</summary>
    public RedisSnapshot BuildInitial()
    {
        var entries = IsString ? new[] { new RedisEntry([], RedisValueCodec.Decode(EditorText, Format)) }
            : Rows.Select(row => ReadRow(row, Format)).ToArray();
        if (!HasValue && !IsString) entries = entries.Select(entry => entry with { Value = [] }).ToArray();
        var snapshot = new RedisSnapshot(SelectedType, IsString ? entries[0].Value.Length : SelectedType == "stream" ? 1 : entries.Length, -1, entries, true);
        RedisDraftValidation.Validate(SelectedType == "stream" ? snapshot with { Type = "hash" } : snapshot);
        if (entries.Length > RedisSnapshot.MaximumEntries || entries.Sum(entry => (long)entry.Name.Length + entry.Value.Length) > RedisSnapshot.MaximumBytes)
            throw new RedisException("RedisValueTooLarge", "REDIS_LIMIT");
        return snapshot;
    }
    private async Task SubmitAsync()
    {
        KeyError = Key.Length == 0 ? Text.Get("RedisCreateKeyRequired") : "";
        ExpiryError = ""; Error = ""; HasConflict = false;
        TimeSpan? expiry = null;
        if (HasExpiry)
        {
            if (!long.TryParse(ExpirySeconds, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds) || seconds <= 0 || seconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
                ExpiryError = Text.Get("RedisCreateExpiryInvalid");
            else expiry = TimeSpan.FromSeconds(seconds);
        }
        Refresh(); if (KeyError.Length > 0 || ExpiryError.Length > 0) return;
        try
        {
            var initial = BuildInitial(); SubmittedKey = RedisKeyId.FromText(Key);
            IsBusy = true; Refresh();
            await _session.CreateAsync(Database, SubmittedKey, initial, expiry, _lifetime.Token);
            CreatedKey = SubmittedKey;
        }
        catch (Exception error)
        {
            HasUnknownWrite = error is RedisException { Code: "REDIS_WRITE_UNKNOWN" };
            HasConflict = error is RedisException { Code: "REDIS_KEY_EXISTS" };
            Error = Text.Error(error, "RedisCommandFailed");
        }
        finally { IsBusy = false; Refresh(); }
        if (CreatedKey is not null) Created?.Invoke();
    }
    private void AddRow() { Rows.Add(new(new([], [], "0"), Format, () => { })); Refresh(); }
    private RedisEntry ReadRow(RedisEntryViewModel row, string format) => new(HasName ? RedisValueCodec.Decode(row.Name, format) : [],
        HasValue ? RedisValueCodec.Decode(row.Value, format) : [], HasScore ? row.Score : "");
    private void RemoveRow(RedisEntryViewModel? row) { if (row is not null && Rows.Count > 1) Rows.Remove(row); Refresh(); }
    private void Refresh()
    {
        foreach (string property in new[] { nameof(IsString), nameof(IsCollection), nameof(HasName), nameof(HasValue), nameof(HasScore), nameof(NameLabel), nameof(Formats), nameof(Format),
            nameof(IsBusy), nameof(IsEditable), nameof(CanSubmit), nameof(CanClose), nameof(CanAddRow), nameof(Error), nameof(HasError), nameof(KeyError), nameof(ExpiryError), nameof(SubmitLabel),
            nameof(HasUnknownWrite), nameof(HasConflict), nameof(CanInspect), nameof(CreatedKey) }) OnPropertyChanged(property);
        SubmitCommand.NotifyCanExecuteChanged(); AddRowCommand.NotifyCanExecuteChanged(); RemoveRowCommand.NotifyCanExecuteChanged();
    }
    /// <inheritdoc />
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
