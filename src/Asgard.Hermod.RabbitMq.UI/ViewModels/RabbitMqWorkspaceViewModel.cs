using Asgard.Hermod.Agent;
using Asgard.Hermod.RabbitMq.UI.Services;

namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>实例级工作区；请求与页面身份绑定，过期结果不能覆盖切换后的页面。</summary>
public sealed partial class RabbitMqWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly IInstanceConfigurationService _configurations;
    private readonly IRabbitMqSessionFactory _factory;
    private readonly IInstanceEditor _editor;
    private readonly IRabbitMqDialogs _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _read;
    private IRabbitMqSession? _session;
    private Task? _initialization;
    private Task? _refreshLoop;
    private int _revision;
    private int _page = 1;
    private int _total;
    private bool _localPaging;
    private readonly Dictionary<RabbitMqResource, IReadOnlyList<RabbitMqColumn>> _columns = [];
    private RabbitMqRecord? _detail;
    private string _detailName = "";
    private bool _inDetail;
    private bool _inMessages;
    private DateTimeOffset? _updated;

    /// <summary>只创建命令，初始化由公共窗口生命周期显式启动。</summary>
    public RabbitMqWorkspaceViewModel(InstanceItemViewModel instance, IInstanceConfigurationService configurations,
        IRabbitMqSessionFactory factory, IInstanceEditor editor, IRabbitMqDialogs dialogs, IAgentClient? agent = null)
        : base("rabbitmq:" + instance.Id, instance.Name, "brand-rabbitmq", instance.Id)
    {
        Instance = instance; _configurations = configurations; _factory = factory; _editor = editor; _dialogs = dialogs;
        RefreshCommand = new(_ => LoadAsync(), _ => CanRead);
        ReconnectCommand = new(_ => ConnectAsync(), _ => !Connection.IsConnecting && !IsBusy && !IsClosed);
        EditConnectionCommand = new(async _ => { await _editor.EditAsync(InstanceId!); }, _ => !IsBusy);
        NavigateCommand = new(async value => { if (value is RabbitMqNavigation item) { Resource = item.Resource; ResetContext(); await LoadAsync(); } }, _ => !IsDialogOpen && !IsClosed && !(IsMessages && IsBusy));
        SearchCommand = new(async _ => { _page = 1; await LoadAsync(); }, _ => CanRead && IsList);
        PreviousCommand = new(async _ => { _page--; await LoadAsync(); }, _ => CanRead && _page > 1);
        NextCommand = new(async _ => { _page++; await LoadAsync(); }, _ => CanRead && _page < PageCount);
        OpenCommand = new(async _ => { if (SelectedRow is { } row) { _detailName = row.Record.Name; _inDetail = true; _inMessages = false; _detail = row.Record; DetailTab = "overview"; await LoadAsync(); } }, _ => CanRead && SelectedRow is not null && Resource is RabbitMqResource.Queues or RabbitMqResource.Exchanges or RabbitMqResource.Connections or RabbitMqResource.Channels);
        BackCommand = new(async _ => { _inDetail = _inMessages = false; _detail = null; Messages.Clear(); await LoadAsync(); }, _ => !IsBusy && (_inDetail || _inMessages));
        CreateCommand = new(_ => EditResourceAsync(false), _ => CanCreate);
        EditCommand = new(_ => EditResourceAsync(true), _ => CanEdit);
        DeleteCommand = new(_ => DeleteAsync(false), _ => CanDelete);
        PurgeCommand = new(_ => DeleteAsync(true), _ => CanRead && QueueTarget is not null && QueueTarget.String("type") != "stream");
        PublishCommand = new(_ => PublishAsync(), _ => CanRead && Resource is RabbitMqResource.Queues or RabbitMqResource.Exchanges && Target is not null);
        MessagesCommand = new(_ => OpenMessagesAsync(), _ => CanRead && QueueTarget is not null && QueueTarget.String("type") != "stream");
        GetCommand = new(_ => GetAsync(), _ => CanRead && IsMessages && int.TryParse(GetCount, out int count) && count is >= 1 and <= 100 && !_getUnknown);
        CopyCommand = new(_ => CopyMessage(), _ => SelectedMessage is not null);
        ExportCommand = new(_ => ExportMessageAsync(), _ => SelectedMessage is not null);
        ToggleExplorerCommand = new(_ => ExplorerOpen = !ExplorerOpen);
        Assistant = new(agent);
        foreach (var command in Commands) command.ExecutionFailed += (_, error) => Fail(error);
    }
    /// <summary>共用实例身份与环境标签。</summary>
    public InstanceItemViewModel Instance { get; }
    public WorkspaceConnectionViewModel Connection { get; } = new();
    public override WorkspaceConnectionViewModel ConnectionState => Connection;
    public string Endpoint { get; private set; } = "";
    public bool IsConnected => _session?.IsConnected == true && !IsClosed;
    public bool IsBusy { get; private set; }
    public bool IsDialogOpen { get; private set; }
    public bool CanRead => IsConnected && !IsBusy && !IsDialogOpen && !_refreshingScopes;
    public bool IsActive { get; set; } = true;
    public bool ExplorerOpen { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(ExplorerWidth)); OnPropertyChanged(nameof(IsExplorerClosed)); } } } = true;
    /// <summary>侧栏折叠后才在页面标题显示展开按钮。</summary>
    public bool IsExplorerClosed => !ExplorerOpen;
    /// <summary>由实际内容高度限制表格，少量记录时分页紧随表格。</summary>
    public double ListTableMaximum { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(ListTableHeight)); } } = 500;
    /// <summary>表头与行均为 36px，多余空间保留为页面背景。</summary>
    public double ListTableHeight => Math.Min(Math.Max(38, (Rows.Count + 1) * 36 + 2), ListTableMaximum);
    public GridLength ExplorerWidth => new(ExplorerOpen ? 232 : 0);
    public string Error { get; private set; } = "";
    public string Notice { get; private set; } = "";
    public bool HasError => Error.Length > 0;
    public bool HasNotice => Notice.Length > 0 && !IsMessages;
    public RabbitMqResource Resource { get; private set; } = RabbitMqResource.Overview;
    public ObservableCollection<RabbitMqNavigation> Navigation { get; } = [];
    public ObservableCollection<RabbitMqNavigation> AdministrationPages { get; } = [];
    public bool IsAdministration => Resource is RabbitMqResource.Vhosts or RabbitMqResource.Users or RabbitMqResource.Permissions or RabbitMqResource.TopicPermissions or RabbitMqResource.Policies;
    public RabbitMqResource NavigationResource => IsAdministration ? AdministrationPages.FirstOrDefault()?.Resource ?? Resource : Resource;
    public RabbitMqResource AdministrationPage
    {
        get => Resource;
        set { if (value != Resource && AdministrationPages.FirstOrDefault(item => item.Resource == value) is { } item) NavigateCommand.Execute(item); }
    }
    public ObservableCollection<string> Vhosts { get; } = [];
    public ObservableCollection<string> AdministrationVhosts { get; } = [];
    public bool HasAdministrationScope => _session?.IsAdministrator == true && Resource is RabbitMqResource.Permissions or RabbitMqResource.TopicPermissions or RabbitMqResource.Policies;
    public string? AdministrationVhost { get; set { if (IsDialogOpen || !SetProperty(ref field, value)) return; if (_refreshingScopes) return; ResetContext(); if (IsConnected && !Connection.IsConnecting && HasAdministrationScope) _ = LoadAsync(); } }
    private string? ScopeVhost => HasAdministrationScope ? AdministrationVhost : SelectedVhost;
    public string? SelectedVhost
    {
        get; set
        {
            if (IsDialogOpen || !SetProperty(ref field, value)) return;
            if (_refreshingScopes) return; ResetContext(); if (IsConnected && !Connection.IsConnecting) _ = LoadAsync();
        }
    }
    public string Search { get; set => SetProperty(ref field, value); } = "";
    public ObservableCollection<RabbitMqRow> Rows { get; } = [];
    public RabbitMqRow? SelectedRow { get; set { if (SetProperty(ref field, value)) RefreshState(); } }
    public IReadOnlyList<RabbitMqColumn> Columns => ColumnsFor(Resource);
    // 自动刷新保持同一组列对象，保留用户调整过的列宽及表格焦点。
    private IReadOnlyList<RabbitMqColumn> ColumnsFor(RabbitMqResource resource)
    {
        if (!_columns.TryGetValue(resource, out var columns)) _columns[resource] = columns = RabbitMqColumn.For(resource);
        return columns;
    }
    public string Title => IsMessages ? Text.Get("RabbitMqGetMessages") : _inDetail ? _detailName.Length == 0 ? Text.Get("RabbitMqDefaultExchange") : _detailName : Text.Get("RabbitMq" + Resource);
    public string Subtitle => Text.Format("RabbitMqScope", ScopeVhost ?? Text.Get("RabbitMqNoVhost"));
    public bool IsOverview => !_inDetail && !_inMessages && Resource == RabbitMqResource.Overview;
    public bool IsList => !_inDetail && !_inMessages && !IsOverview;
    public bool IsDetails => _inDetail && !_inMessages;
    public bool IsMessages => _inMessages;
    public bool HasBack => _inDetail || _inMessages;
    public bool IsEmpty => IsList && Rows.Count == 0 && !IsBusy;
    public string EmptyText => Text.Get(ScopeVhost is null && Resource is not (RabbitMqResource.Vhosts or RabbitMqResource.Users) ? "RabbitMqNoVhostsHint" : Search.Length > 0 ? "RabbitMqNoMatches" : "RabbitMqEmpty");
    public int PageCount => Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize));
    public IReadOnlyList<int> PageSizes { get; } = [6, 20, 50, 100];
    public int PageSize { get; set { if (value is not (6 or 20 or 50 or 100) || !SetProperty(ref field, value)) return; _page = 1; if (CanRead) _ = LoadAsync(); } } = 50;
    public string PageText => Text.Format("RabbitMqPage", _page, PageCount, _total);
    public string PagingHint => Text.Get(_localPaging ? "RabbitMqLocalPaging" : "RabbitMqServerPaging");
    public string SelectionText => Target is null ? Text.Get("RabbitMqNothingSelected") : Text.Format("RabbitMqSelected", Target.Name.Length > 0 ? Target.Name : Text.Get("RabbitMqDefaultExchange"));
    public string Status => _session is null ? Text.Get("RabbitMqDisconnected") : Text.Format("RabbitMqStatus", _session.Version,
        Text.Get(_session.Options.BaseUri.Scheme == "https" ? "RabbitMqHttpsVerified" : "RabbitMqHttp"), SelectedVhost ?? Text.Get("RabbitMqNoVhost"));
    public string UpdatedText => IsMessages ? SnapshotText : _updated is null ? Text.Get("RabbitMqNotUpdated")
        : Text.Format(!IsConnected ? "RabbitMqSnapshotDisconnected" : RefreshInterval == "off" ? "RabbitMqUpdatedManual" : "RabbitMqUpdatedInterval", _updated.Value.ToLocalTime().ToString("T", CultureInfo.CurrentCulture), RefreshInterval);
    private RabbitMqRecord? Target => _inDetail || _inMessages ? _detail : SelectedRow?.Record;
    private RabbitMqRecord? QueueTarget => Resource == RabbitMqResource.Queues ? Target : null;
    public RabbitMqAssistantViewModel Assistant { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }
    public AsyncRelayCommand EditConnectionCommand { get; }
    public AsyncRelayCommand NavigateCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand BackCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand PurgeCommand { get; }
    public AsyncRelayCommand PublishCommand { get; }
    public AsyncRelayCommand MessagesCommand { get; }
    public AsyncRelayCommand GetCommand { get; }
    public RelayCommand CopyCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand ToggleExplorerCommand { get; }
    private IEnumerable<AsyncRelayCommand> Commands => [RefreshCommand, ReconnectCommand, EditConnectionCommand, NavigateCommand, SearchCommand, PreviousCommand, NextCommand, OpenCommand, BackCommand, CreateCommand, EditCommand, DeleteCommand, PurgeCommand, PublishCommand, MessagesCommand, GetCommand, ExportCommand];
    private void RefreshState()
    {
        OnPropertyChanged(nameof(ListTableHeight)); OnPropertyChanged(nameof(PropertyRows)); OnPropertyChanged(nameof(PropertiesHeight)); OnPropertyChanged(nameof(HasHeaderRefresh)); OnPropertyChanged(nameof(RateSummary));
        OnPropertyChanged(nameof(HasAdministrationScope)); OnPropertyChanged(nameof(HasNoBrowseScope));
        OnPropertyChanged(nameof(IsAdministration)); OnPropertyChanged(nameof(NavigationResource)); OnPropertyChanged(nameof(AdministrationPage));
        OnPropertyChanged(nameof(HasTypeFilter)); OnPropertyChanged(nameof(TypeFilters));
        foreach (string name in new[] { nameof(IsConnected), nameof(CanRead), nameof(IsBusy), nameof(IsDialogOpen), nameof(HasError), nameof(Error), nameof(Notice), nameof(HasNotice), nameof(Resource), nameof(Title), nameof(Subtitle), nameof(Columns), nameof(IsOverview), nameof(IsList), nameof(IsDetails), nameof(IsMessages), nameof(HasBack), nameof(IsEmpty), nameof(EmptyText), nameof(PageText), nameof(PagingHint), nameof(SelectionText), nameof(Status), nameof(UpdatedText), nameof(Endpoint), nameof(CanCreate), nameof(CanEdit), nameof(CanDelete), nameof(HasQueueActions), nameof(HasPublish), nameof(CreateLabel), nameof(DeleteLabel), nameof(DetailJson), nameof(DetailRows), nameof(DetailColumns), nameof(DetailTabs), nameof(Metrics), nameof(DetailChart), nameof(HasChart), nameof(HasNodes), nameof(SnapshotText), nameof(Payload), nameof(Properties), nameof(Headers), nameof(TruncationText), nameof(IsTruncated), nameof(CopyLabel), nameof(ExportLabel) }) OnPropertyChanged(name);
        foreach (var command in Commands) command?.NotifyCanExecuteChanged(); CopyCommand?.NotifyCanExecuteChanged();
    }
    public bool HasNoBrowseScope => Vhosts.Count == 0;
    private void ResetContext()
    {
        _read?.Cancel(); _revision++; _page = 1; _typeFilter = "all"; OnPropertyChanged(nameof(TypeFilter)); Search = ""; _inDetail = _inMessages = false; _detail = null; Rows.Clear(); Messages.Clear(); SelectedRow = null; Notice = ""; Error = ""; RefreshState();
    }
    private void Fail(Exception error)
    {
        Error = Text.Error(error, "RabbitMqRequestFailed");
        if (error is RabbitMqFailure { ConnectionLost: true }) Connection.Disconnected(Error);
        RefreshState();
    }
}
