namespace Asgard.Hermod.UI.ViewModels;

/// <summary>组合系统设置导航与标签管理，负责窗口范围内的订阅生命周期。</summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private SettingsCategoryViewModel _selectedCategory;
    private readonly ILanguagePreferenceStore? _languageStore;
    private Asgard.Hermod.Abstractions.Localization.LanguageOption _selectedLanguage;
    private string _languageMessage = "";
    private readonly IAccountService? _accounts;
    private readonly IConfigurationSyncService? _sync;
    private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    // 设置语言在重启后应用；后台通知不应改变已打开窗口的资源语言。
    private readonly string _signedInStatus = Text.Get("SignedIn");
    private readonly string _signedOutStatus = Text.Get("NotSignedIn");
    private readonly string _signedInWorkspace = Text.Get("SyncCloudWorkspace");
    private readonly string _signedOutWorkspace = Text.Get("LocalWorkspace");
    private bool _disposed;

    /// <summary>由宿主提供统一标签来源和子窗口服务。</summary>
    public SettingsViewModel(ITagManagementService tags, ITagDialogService dialogs,
        ILanguagePreferenceStore? languageStore = null, IFileOpenRuleStore? fileRules = null,
        ITemporaryFileStorage? temporaryFiles = null, IStorageSettingsDialogs? storageDialogs = null,
        IAccountService? accounts = null, Func<Task>? openAccount = null, IMainWindowClosePreferenceStore? closePreferences = null,
        Agent.AgentConfiguration? agentConfiguration = null, IAgentPreferenceStore? agentPreferences = null,
        Agent.AgentModelCatalog? modelCatalog = null, Agent.IAgentClient? agentClient = null,
        ApplicationUpdateViewModel? updates = null, IConfigurationSyncService? automaticSync = null, Func<Task>? resolveConflicts = null)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(dialogs);
        Tags = new TagManagementViewModel(tags, dialogs);
        Updates = updates;
        _languageStore = languageStore;
        CloseSettings = closePreferences is null ? null : new(closePreferences);
        AgentSettings = modelCatalog is null && agentConfiguration is not null && agentPreferences is not null ? new(agentConfiguration, agentPreferences) : null;
        AgentModels = modelCatalog is not null && agentClient is not null ? new(modelCatalog, agentClient) : null;
        _accounts = accounts; _sync = automaticSync;
        ResolveConflictsCommand = new AsyncRelayCommand(_ => resolveConflicts?.Invoke() ?? Task.CompletedTask,
            _ => !_disposed && HasSyncConflict && resolveConflicts is not null);
        if (_sync is not null) _sync.Changed += OnSyncChanged;
        OpenAccountCommand = new AsyncRelayCommand(_ => openAccount?.Invoke() ?? Task.CompletedTask,
            _ => !_disposed && accounts is not null && openAccount is not null);
        if (accounts is not null) accounts.AccountChanged += OnAccountChanged;
        FileOpening = fileRules is null ? null : new FileOpeningSettingsViewModel(fileRules);
        Storage = temporaryFiles is not null && storageDialogs is not null ? new StorageSettingsViewModel(temporaryFiles, storageDialogs) : null;
        Categories =
        [
            new("general", Text.Get("General"), "sliders-horizontal", languageStore is not null || closePreferences is not null),
            new("agent", Text.Get("AgentSettingsTitle"), "sparkles", AgentModels is not null || AgentSettings is not null),
            new("tags", Text.Get("TagManagement"), "tags", true),
            new("storage", Text.Get("Storage"), "hard-drive", Storage is not null),
            new("file-opening", Text.Get("FileOpening"), "folder-open", fileRules is not null),
            new("account", Text.Get("AccountAndSync"), "user-round", accounts is not null && openAccount is not null),
            new("updates", Text.Get("UpdatesTitle"), "download", updates is not null),
            new("about", Text.Get("About"), "info", true),
        ];
        Categories = Categories.Where(category => category.IsAvailable).ToArray();
        Languages = [new("", Text.Get("FollowWindows"), []), .. Text.Languages];
        _selectedLanguage = Languages[0];
        try
        {
            var saved = languageStore?.Load();
            _selectedLanguage = string.IsNullOrWhiteSpace(saved) ? Languages[0]
                : Languages.FirstOrDefault(language => language.Id == saved) ?? Languages.Single(language => language.Id == "en");
        }
        catch (Exception exception) { _languageMessage = Text.Error(exception); }
        _selectedCategory = Categories.Single(category => category.Id == (languageStore is null && closePreferences is null ? "tags" : "general"));
        SelectCategoryCommand = new RelayCommand(parameter =>
        {
            if (parameter is SettingsCategoryViewModel category && Categories.Contains(category))
                SelectedCategory = category;
        });
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));
    }

    /// <summary>请求关闭设置窗口。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>只展示已装配功能的左侧导航，关于始终位于最后。</summary>
    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; }
    /// <summary>本机关闭询问和默认动作，保存后立即作用于主窗体。</summary>
    public MainWindowCloseSettingsViewModel? CloseSettings { get; }
    /// <summary>本机模型配置，与账号及实例同步独立。</summary>
    public AgentSettingsViewModel? AgentSettings { get; }
    /// <summary>当前账号的命名模型配置管理。</summary>
    public AgentModelsViewModel? AgentModels { get; }
    /// <summary>AI 设置使用稳定导航标识。</summary>
    public bool IsAgentSelected => SelectedCategory.Id == "agent";

    /// <summary>系统选项和语言原名组成稳定选择列表，新增语种无需修改设置视图。</summary>
    public IReadOnlyList<Asgard.Hermod.Abstractions.Localization.LanguageOption> Languages { get; }
    /// <summary>仅在持久化成功后确认选择；不在传输和终端运行中重建工作区。</summary>
    public Asgard.Hermod.Abstractions.Localization.LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || value == _selectedLanguage || !Languages.Contains(value)) return;
            try
            {
                if (_languageStore is null) return;
                _languageStore.Save(value.Id.Length == 0 ? null : value.Id);
                SetProperty(ref _selectedLanguage, value);
                LanguageMessage = Text.Get("LanguagePreferenceSavedRestartHermodToApplyIt");
            }
            catch (Exception)
            {
                LanguageMessage = Text.Get("CannotSaveTheLanguagePreferenceTryAgain");
                OnPropertyChanged(nameof(SelectedLanguage));
            }
        }
    }
    /// <summary>保存结果与失败提示不包含内部数据库异常。</summary>
    public string LanguageMessage { get => _languageMessage; private set => SetProperty(ref _languageMessage, value); }
    /// <summary>当前已经应用的语言与下一次启动的偏好分开展示。</summary>
    public string CurrentLanguage => Text.Format("CurrentLanguage", Text.Languages.Single(language => language.Id == Text.Culture.Name).NativeName);
    /// <summary>控制通用页面可见性。</summary>
    public bool IsGeneralSelected => SelectedCategory.Id == "general";
    /// <summary>控制标签管理页面可见性。</summary>
    public bool IsTagsSelected => SelectedCategory.Id == "tags";
    /// <summary>文件规则页面的可见性使用稳定分类标识。</summary>
    public bool IsFileOpeningSelected => SelectedCategory.Id == "file-opening";
    /// <summary>存储设置仅在管理服务已装配时开放。</summary>
    public bool IsStorageSelected => SelectedCategory.Id == "storage";
    /// <summary>账号入口复用宿主的登录与同步管理，不维护另一份登录状态。</summary>
    public bool IsAccountSelected => SelectedCategory.Id == "account";
    /// <summary>关于页通过固定分类标识控制可见性。</summary>
    public bool IsAboutSelected => SelectedCategory.Id == "about";
    /// <summary>更新详情使用独立的非模态设置分类。</summary>
    public bool IsUpdatesSelected => SelectedCategory.Id == "updates";
    /// <summary>应用级更新状态不随设置窗口关闭释放。</summary>
    public ApplicationUpdateViewModel? Updates { get; }
    /// <summary>版本、作者、许可与源码入口统一归于关于页。</summary>
    public AboutViewModel About { get; } = new();
    /// <summary>只显示已验证的 JWT 用户名。</summary>
    public string AccountName => _accounts?.Account?.Username ?? _signedOutStatus;
    /// <summary>登录状态与云端同步是否成功分开展示。</summary>
    public string AccountStatus => _accounts?.Account is not null ? _signedInStatus : _signedOutStatus;
    /// <summary>设置与主窗口使用同一账号确定工作区归属。</summary>
    public string WorkspaceName => _accounts?.Account is not null ? _signedInWorkspace : _signedOutWorkspace;
    /// <summary>侧栏图标跟随真实工作区归属。</summary>
    public string WorkspaceIcon => _accounts?.Account is not null ? "cloud" : "laptop";
    /// <summary>打开已有账号与同步管理，设置页不自动发起浏览器登录。</summary>
    public AsyncRelayCommand OpenAccountCommand { get; }
    /// <summary>账户设置直接显示同步状态，登录成功不能代替同步成功。</summary>
    public bool HasSyncStatus => _accounts?.Account is not null && _sync is not null;
    /// <summary>冲突状态由稳定枚举决定，不依赖文案。</summary>
    public bool HasSyncConflict => HasSyncStatus && _sync?.Status == ConfigurationSyncStatus.Conflict;
    /// <summary>服务提供的完整安全同步说明。</summary>
    public string SyncMessage => HasSyncStatus ? _sync?.Message ?? "" : "";
    /// <summary>从设置窗口直接打开所属账户的冲突列表。</summary>
    public AsyncRelayCommand ResolveConflictsCommand { get; }
    /// <summary>本机临时文件的统计和清理状态。</summary>
    public StorageSettingsViewModel? Storage { get; }
    /// <summary>可选的本机文件规则管理状态。</summary>
    public FileOpeningSettingsViewModel? FileOpening { get; }
    /// <summary>当前设置分类。</summary>
    public SettingsCategoryViewModel SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is null || !Categories.Contains(value) || !SetProperty(ref _selectedCategory, value)) return;
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsAgentSelected));
            OnPropertyChanged(nameof(IsTagsSelected));
            OnPropertyChanged(nameof(IsFileOpeningSelected));
            OnPropertyChanged(nameof(IsStorageSelected));
            OnPropertyChanged(nameof(IsAccountSelected));
            OnPropertyChanged(nameof(IsAboutSelected));
            OnPropertyChanged(nameof(IsUpdatesSelected));
        }
    }
    /// <summary>可复用的标签管理状态。</summary>
    public TagManagementViewModel Tags { get; }
    /// <summary>导航入口只允许切换已实现的分类。</summary>
    public RelayCommand SelectCategoryCommand { get; }
    /// <summary>关闭当前设置窗口。</summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>宿主可在显示窗口前完成首次读取，失败信息由管理页面呈现。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.WhenAll(
        Tags.InitializeAsync(cancellationToken), Storage?.InitializeAsync(cancellationToken) ?? Task.CompletedTask);

    /// <summary>窗口关闭时释放后台标签通知。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_accounts is not null) _accounts.AccountChanged -= OnAccountChanged;
        OpenAccountCommand.NotifyCanExecuteChanged(); ResolveConflictsCommand.NotifyCanExecuteChanged();
        if (_sync is not null) _sync.Changed -= OnSyncChanged;
        Tags.Dispose(); Storage?.Dispose(); AgentSettings?.Dispose(); AgentModels?.Dispose();
    }

    /// <summary>后台账号通知切回设置窗口线程，关闭后的排队通知不再更新绑定。</summary>
    private void OnAccountChanged(object? sender, EventArgs args)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => OnAccountChanged(sender, args)); return; }
        if (_disposed) return;
        OnPropertyChanged(nameof(AccountName)); OnPropertyChanged(nameof(AccountStatus));
        OnPropertyChanged(nameof(WorkspaceName)); OnPropertyChanged(nameof(WorkspaceIcon));
        OnSyncChanged(sender, args);
    }

    /// <summary>设置始终跟随同一同步服务，关闭后不再响应后台事件。</summary>
    private void OnSyncChanged(object? sender, EventArgs args)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => OnSyncChanged(sender, args)); return; }
        if (_disposed) return;
        OnPropertyChanged(nameof(HasSyncStatus)); OnPropertyChanged(nameof(HasSyncConflict)); OnPropertyChanged(nameof(SyncMessage));
        ResolveConflictsCommand.NotifyCanExecuteChanged();
    }
}
