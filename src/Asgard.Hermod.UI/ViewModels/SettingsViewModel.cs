namespace Asgard.Hermod.UI.ViewModels;

/// <summary>组合系统设置导航与标签管理，负责窗口范围内的订阅生命周期。</summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private SettingsCategoryViewModel _selectedCategory;

    /// <summary>由宿主提供统一标签来源和子窗口服务。</summary>
    public SettingsViewModel(ITagManagementService tags, ITagDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(dialogs);
        Tags = new TagManagementViewModel(tags, dialogs);
        _selectedCategory = Categories.Single(category => category.Id == "tags");
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
    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; } =
    [
        new("general", "通用", "sliders-horizontal", false),
        new("appearance", "外观", "palette", false),
        new("tags", "Tag 管理", "tags", true),
        new("storage", "存储", "hard-drive", false),
        new("account", "账户", "user-round", false),
        new("shortcuts", "快捷键", "keyboard", false),
        new("about", "关于", "info", false),
    ];
    /// <summary>当前设置分类。</summary>
    public SettingsCategoryViewModel SelectedCategory
    {
        get => _selectedCategory;
        private set => SetProperty(ref _selectedCategory, value);
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
