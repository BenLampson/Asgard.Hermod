using System.Diagnostics;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>首页仅呈现安全实例快照，打开动作不代表远程连接成功。</summary>
public sealed class HomeWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly IRecentInstanceStore? _store;
    private readonly IInstanceProviderRegistry? _registry;
    private readonly List<RecentInstance> _history = [];
    private IReadOnlyList<InstanceItemViewModel> _instances = [];
    private string _message = "";

    /// <summary>最近访问读取失败不阻止离线工作区启动。</summary>
    public HomeWorkspaceViewModel(Action<InstanceItemViewModel> openInstance,
        IInstanceProviderRegistry? registry = null, IRecentInstanceStore? store = null)
        : base("home", "", "house", canClose: false)
    {
        ArgumentNullException.ThrowIfNull(openInstance);
        _registry = registry;
        _store = store;
        try { _history.AddRange((store?.Load() ?? []).Take(RecentInstance.MaximumCount)); }
        catch (Exception) { Message = Text.Get("HomeHistoryLoadFailed"); }
        OpenRecentCommand = new RelayCommand(value =>
        {
            if (value is RecentInstanceViewModel recent && _instances.Any(item => item.Id == recent.Instance.Id))
                openInstance(recent.Instance);
        }, value => value is RecentInstanceViewModel recent && _instances.Any(item => item.Id == recent.Instance.Id));
        OpenGitHubCommand = new RelayCommand(_ =>
        {
            try { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); }
            catch (Exception) { Message = Text.Get("HomeOpenRepositoryFailed"); }
        });
    }

    /// <summary>公开项目地址固定来自仓库，不接受用户输入作为启动参数。</summary>
    public string RepositoryUrl => "https://github.com/BenLampson/Asgard.Hermod";
    /// <summary>显示于软件简介中的程序集版本。</summary>
    public string Version => "v" + (typeof(HomeWorkspaceViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
    /// <summary>已配置实例数，不计内置本地终端。</summary>
    public string InstanceCount => Text.Format("HomeInstanceCount", _instances.Count(item => !item.IsLocal));
    /// <summary>真实注册类型与已有配置中的未知类型均保留。</summary>
    public ObservableCollection<HomeInstanceTypeViewModel> Types { get; } = [];
    /// <summary>最近打开的六个仍存在的实例。</summary>
    public ObservableCollection<RecentInstanceViewModel> RecentInstances { get; } = [];
    /// <summary>空记录引导。</summary>
    public bool HasNoRecentInstances => RecentInstances.Count == 0;
    /// <summary>存在有效访问记录时将最近实例放在介绍和统计之前。</summary>
    public bool HasRecentInstances => !HasNoRecentInstances;
    /// <summary>安全且本地化的持久化或浏览器失败提示。</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>重用外壳工作页入口。</summary>
    public RelayCommand OpenRecentCommand { get; }
    /// <summary>在系统默认浏览器打开公开仓库。</summary>
    public RelayCommand OpenGitHubCommand { get; }
    /// <summary>首页与侧栏共用新建实例入口。</summary>
    public ICommand? AddInstanceCommand { get; internal set; }
    /// <summary>首页与侧栏共用本地终端入口。</summary>
    public ICommand? OpenLocalTerminalCommand { get; internal set; }

    /// <summary>更新统计与最近快照；删除实例自动隐藏对应访问记录。</summary>
    public void Refresh(IReadOnlyList<InstanceItemViewModel> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        // 本地终端是内置能力，不依赖 SQLite 中存在实例配置。
        _instances = instances.Any(item => item.IsLocal) ? instances : instances.Concat([
            new InstanceItemViewModel(new("local-shell", "sandbox-local", Text.Get("LocalTerminal"), "terminal", ["Local"], true))
        ]).ToArray();
        Types.Clear();
        var registeredIds = (_registry?.Types ?? []).Select(type => type.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var type in _registry?.Types ?? [])
            Types.Add(new(type.Id, type.Name, type.Icon, instances.Count(item => !item.IsLocal && item.Summary.TypeId == type.Id)));
        foreach (var group in instances.Where(item => !item.IsLocal && !registeredIds.Contains(item.Summary.TypeId))
            .GroupBy(item => item.Summary.TypeId))
            Types.Add(new(group.Key, Text.Get("HomeOtherInstances"), group.First().Icon, group.Count()));
        OnPropertyChanged(nameof(InstanceCount));
        RefreshRecent();
    }

    /// <summary>仅在用户主动打开实例时记录，不把启动预建页签算作访问。</summary>
    public void RecordOpened(InstanceItemViewModel instance)
    {
        _history.RemoveAll(item => item.InstanceId == instance.Id);
        _history.Insert(0, new(instance.Id, DateTimeOffset.UtcNow));
        if (_history.Count > RecentInstance.MaximumCount) _history.RemoveRange(RecentInstance.MaximumCount, _history.Count - RecentInstance.MaximumCount);
        RefreshRecent();
        try { _store?.Save(_history); }
        catch (Exception) { Message = Text.Get("HomeHistorySaveFailed"); }
    }

    /// <summary>始终从最新导航快照取名称，不持久化或展示连接参数。</summary>
    private void RefreshRecent()
    {
        RecentInstances.Clear();
        foreach (var record in _history)
            if (_instances.FirstOrDefault(item => item.Id == record.InstanceId) is { } instance)
                RecentInstances.Add(new(instance, record.OpenedAt));
        OnPropertyChanged(nameof(HasNoRecentInstances));
        OnPropertyChanged(nameof(HasRecentInstances));
        OpenRecentCommand.NotifyCanExecuteChanged();
    }
}
