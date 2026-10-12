namespace Asgard.Hermod.UI.ViewModels;

/// <summary>组合系统设置导航与标签管理，负责窗口范围内的订阅生命周期。</summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private SettingsCategoryViewModel _selectedCategory;
    private readonly ILanguagePreferenceStore? _languageStore;
    private Asgard.Hermod.Abstractions.Localization.LanguageOption _selectedLanguage;
    private string _languageMessage = "";

    /// <summary>由宿主提供统一标签来源和子窗口服务。</summary>
    public SettingsViewModel(ITagManagementService tags, ITagDialogService dialogs,
        ILanguagePreferenceStore? languageStore = null)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(dialogs);
        Tags = new TagManagementViewModel(tags, dialogs);
        _languageStore = languageStore;
        Categories =
        [
            new("general", Text.Get("General"), "sliders-horizontal", languageStore is not null),
            new("appearance", Text.Get("Appearance"), "palette", false),
            new("tags", Text.Get("TagManagement"), "tags", true),
            new("storage", Text.Get("Storage"), "hard-drive", false),
            new("account", Text.Get("Account"), "user-round", false),
            new("shortcuts", Text.Get("KeyboardShortcuts"), "keyboard", false),
            new("about", Text.Get("About"), "info", false),
        ];
        Languages = [new("", Text.Get("FollowWindows"), []), .. Text.Languages];
        _selectedLanguage = Languages[0];
        try
        {
            var saved = languageStore?.Load();
            _selectedLanguage = string.IsNullOrWhiteSpace(saved) ? Languages[0]
                : Languages.FirstOrDefault(language => language.Id == saved) ?? Languages.Single(language => language.Id == "en");
        }
        catch (Exception exception) { _languageMessage = Text.Error(exception); }
        _selectedCategory = Categories.Single(category => category.Id == (languageStore is null ? "tags" : "general"));
        SelectCategoryCommand = new RelayCommand(parameter =>
        {
            if (parameter is SettingsCategoryViewModel { IsAvailable: true } category)
                SelectedCategory = category;
        });
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));
    }

    /// <summary>请求关闭设置窗口。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>设计稿中的完整分类导航，未设计的页面保持不可用。</summary>
    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; }

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
    /// <summary>当前设置分类。</summary>
    public SettingsCategoryViewModel SelectedCategory
    {
        get => _selectedCategory;
        private set
        {
            if (!SetProperty(ref _selectedCategory, value)) return;
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsTagsSelected));
        }
    }
    /// <summary>可复用的标签管理状态。</summary>
    public TagManagementViewModel Tags { get; }
    /// <summary>导航入口只允许切换已实现的分类。</summary>
    public RelayCommand SelectCategoryCommand { get; }
    /// <summary>关闭当前设置窗口。</summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>宿主可在显示窗口前完成首次读取，失败信息由管理页面呈现。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Tags.InitializeAsync(cancellationToken);

    /// <summary>窗口关闭时释放后台标签通知。</summary>
    public void Dispose() => Tags.Dispose();
}
