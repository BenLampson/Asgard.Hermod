namespace Asgard.Hermod.UI.ViewModels;

/// <summary>管理可切换工作页，保证页签关闭和应用退出时释放终端。</summary>
public sealed class WorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IWorkspaceDataSource _source;
    private readonly ITerminalSessionFactory _terminalFactory;
    private WorkspacePageViewModel _selectedTab;
    private Task? _disposeTask;
    private int _terminalNumber;

    /// <summary>建立 Pencil 中的初始页签，但不自动连接任何服务。</summary>
    public WorkspaceViewModel(IWorkspaceDataSource source, ITerminalSessionFactory terminalFactory)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(terminalFactory);
        _source = source;
        _terminalFactory = terminalFactory;
        _selectedTab = new EmptyWorkspaceViewModel { IsSelected = true };
        Tabs.Add(_selectedTab);
        foreach (var instance in source.GetInstances().Where(instance => !instance.IsLocal).Take(3))
            Tabs.Add(CreateServicePage(new InstanceItemViewModel(instance)));
        Tabs.Add(new EmptyWorkspaceViewModel("tag:production", "Production", "tag"));
        SelectTabCommand = new RelayCommand(value =>
        {
            if (value is WorkspacePageViewModel page && Tabs.Contains(page) && !page.IsClosed)
                SelectedTab = page;
        });
        CloseTabCommand = new AsyncRelayCommand(value =>
            value is WorkspacePageViewModel page ? CloseTabAsync(page) : Task.CompletedTask);
    }

    /// <summary>活动页变化后同步侧栏状态。</summary>
    public event EventHandler? SelectedTabChanged;
    /// <summary>按显示顺序排列的页签。</summary>
    public ObservableCollection<WorkspacePageViewModel> Tabs { get; } = [];
    /// <summary>当前工作页。</summary>
    public WorkspacePageViewModel SelectedTab
    {
        get => _selectedTab;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!Tabs.Contains(value) || value.IsClosed || ReferenceEquals(_selectedTab, value))
                return;
            _selectedTab.IsSelected = false;
            _selectedTab = value;
            _selectedTab.IsSelected = true;
            OnPropertyChanged();
            SelectedTabChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    /// <summary>切换页签。</summary>
    public RelayCommand SelectTabCommand { get; }
    /// <summary>关闭页签并等待资源释放。</summary>
    public AsyncRelayCommand CloseTabCommand { get; }

    /// <summary>已有实例页优先复用；本地实例才创建真实终端。</summary>
    public void OpenInstance(InstanceItemViewModel instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
        WorkspacePageViewModel? page = Tabs.FirstOrDefault(tab => tab.InstanceId == instance.Id && !tab.IsClosed);
        if (page is null)
        {
            page = instance.IsLocal ? CreateTerminalPage(instance) : CreateServicePage(instance);
            Tabs.Add(page);
        }
        SelectedTab = page;
    }

    /// <summary>用户主动请求时创建一个新的本地终端页。</summary>
    public void OpenLocalTerminal()
    {
        ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
        var local = new InstanceItemViewModel(_source.GetInstances().FirstOrDefault(instance => instance.IsLocal)
            ?? new InstanceSummary("local-shell", "sandbox-local", "Local terminal", "laptop", ["Local"], true));
        var page = CreateTerminalPage(local);
        Tabs.Add(page);
        SelectedTab = page;
    }

    /// <summary>关闭活动页时选择邻近页，常驻首页和分组保持存在。</summary>
    public async Task CloseTabAsync(WorkspacePageViewModel page)
    {
        if (!page.CanClose || !Tabs.Contains(page))
            return;
        int index = Tabs.IndexOf(page);
        if (ReferenceEquals(SelectedTab, page))
            SelectedTab = Tabs[index > 0 ? index - 1 : 1];
        Tabs.Remove(page);
        await page.CloseAsync();
    }

    /// <summary>通过应用层读取详情快照。</summary>
    private ServiceDetailViewModel CreateServicePage(InstanceItemViewModel instance) =>
        new(instance, _source.GetServiceSnapshot(instance.Id));

    /// <summary>每个新终端拥有独立页签和应用层会话句柄。</summary>
    private TerminalWorkspaceViewModel CreateTerminalPage(InstanceItemViewModel instance) =>
        new("terminal:" + ++_terminalNumber, instance, _terminalFactory);

    /// <summary>应用退出时关闭所有页，包括常驻导航页。</summary>
    public ValueTask DisposeAsync()
    {
        _disposeTask ??= CloseAllAsync();
        return new ValueTask(_disposeTask);
    }

    /// <summary>统一启动关闭，让所有终端的清理都得到等待。</summary>
    private async Task CloseAllAsync()
    {
        Task[] closing = Tabs.Select(page => page.CloseAsync()).ToArray();
        Tabs.Clear();
        await Task.WhenAll(closing);
    }
}
