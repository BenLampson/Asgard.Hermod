using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>参数化 Docker 命令库；预览、确认和执行绑定同一份不可变计划。</summary>
public sealed partial class SshDockerCommandsViewModel : ObservableObject, IDisposable
{
    private readonly Func<ISshWorkspaceConnection?> _connection;
    private readonly Func<string> _host;
    private readonly Func<bool> _canInsert;
    private readonly Func<SshDockerCommandPlan, CancellationToken, Task> _terminal;
    private readonly ISshWorkspaceDialogs _dialogs;
    private readonly SshDockerEnvironmentReader _environmentReader;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;
    private SshDockerCommandPlan? _plan;

    /// <summary>复用工作区传输与环境缓存，不持有凭据，不读取交互 Shell 历史。</summary>
    public SshDockerCommandsViewModel(Func<ISshWorkspaceConnection?> connection, Func<string> host, ISshWorkspaceDialogs dialogs,
        Func<bool> canInsert, Func<SshDockerCommandPlan, CancellationToken, Task> terminal, SshDockerEnvironmentReader? environmentReader = null)
    {
        _connection = connection; _host = host; _dialogs = dialogs; _canInsert = canInsert; _terminal = terminal; _environmentReader = environmentReader ?? new();
        Operations = CollectionViewSource.GetDefaultView(SshDockerCommandBuilder.Operations.Select(o => new SshDockerOperationItem(o)).ToArray());
        Operations.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SshDockerOperationItem.Group)));
        Operations.Filter = item => item is SshDockerOperationItem o && (Search.Length == 0 || o.Title.Contains(Search, StringComparison.OrdinalIgnoreCase));
        ContainerView = CollectionViewSource.GetDefaultView(Containers);
        ContainerView.Filter = item => item is SshMonitorContainerItem c && (ContainerState.Length == 0 || c.State == ContainerState) && (ContainerSearch.Length == 0 ||
            c.Name.Contains(ContainerSearch, StringComparison.OrdinalIgnoreCase) || c.Image.Contains(ContainerSearch, StringComparison.OrdinalIgnoreCase));
        SelectedOperation = SshDockerCommandBuilder.Operations.Select(o => new SshDockerOperationItem(o)).First(o => o.Id == "logs");
        DetectCommand = new AsyncRelayCommand(_ => DetectAsync(true), _ => !IsBusy && _connection() is not null && !_disposed);
        ExecuteCommand = new AsyncRelayCommand(_ => ExecuteAsync(), _ => CanExecute);
        ActionCommand = new AsyncRelayCommand(p => RunActionAsync(p as string ?? ""), p => CanRunAction(p as string));
        ShowDetailsCommand = new RelayCommand(_ => { ShowDetails = true; DetailSection = "details"; }, _ => SelectedContainer is not null);
        SelectToolCommand = new RelayCommand(value => { if (value is string section) ToolSection = section; }, _ => !IsBusy);
        SelectDetailCommand = new RelayCommand(value => { if (value is string section) DetailSection = section; });
        InsertCommand = new RelayCommand(_ => { if (_plan is { } plan && CanInsert) InsertRequested?.Invoke(this, plan.Command); }, _ => CanInsert);
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
    }

    /// <summary>本地筛选与分组的操作目录。</summary>
    public ICollectionView Operations { get; }
    /// <summary>容器列表在本地筛选，不改变远端资源。</summary>
    public ICollectionView ContainerView { get; }
    /// <summary>镜像与 Compose 为折叠的次级工具，常用容器动作无需进入目录。</summary>
    public IReadOnlyList<SshDockerOperationItem> OtherOperations { get; } = SshDockerCommandBuilder.Operations
        .Where(o => o.Id is "images" or "pull" || o.RequiresCompose).Select(o => new SshDockerOperationItem(o)).ToArray();
    /// <summary>次级资源的选择与常用容器动作分开，避免下拉框清空主操作。</summary>
    public SshDockerOperationItem? SelectedOtherOperation
    {
        get;
        set { SetProperty(ref field, value); if (value is not null) SelectedOperation = value; }
    }
    /// <summary>按容器名称或镜像筛选；隐藏的容器不保留为操作目标。</summary>
    public string ContainerSearch
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;
            ContainerView.Refresh();
            if (SelectedContainer is { } selected && !ContainerView.Contains(selected)) SelectedContainer = null;
            OnPropertyChanged(nameof(HasNoContainers));
        }
    } = "";
    /// <summary>当前连接真实容器列表。</summary>
    public ObservableCollection<SshMonitorContainerItem> Containers { get; } = [];
    /// <summary>只有显式插入动作才通知终端，字符串不带回车。</summary>
    public event EventHandler<string>? InsertRequested;
    /// <summary>已验证的变更完成后由宿主刷新监测。</summary>
    public event EventHandler? Changed;
    /// <summary>打开或重新检测环境。</summary>
    public AsyncRelayCommand DetectCommand { get; }
    /// <summary>执行预览中的操作。</summary>
    public AsyncRelayCommand ExecuteCommand { get; }
    /// <summary>容器动作直接执行或进入人类可读的确认，不要求用户理解命令。</summary>
    public AsyncRelayCommand ActionCommand { get; }
    /// <summary>详情仅展示已读取的容器字段。</summary>
    public RelayCommand ShowDetailsCommand { get; }
    /// <summary>可选的容器详情区域。</summary>
    public bool ShowDetails { get; set => SetProperty(ref field, value); }
    /// <summary>插入终端但不执行。</summary>
    public RelayCommand InsertCommand { get; }
    /// <summary>取消等待，不保证回滚已经派发的远端操作。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>当前连接的能力，不从用户填写的版本推断。</summary>
    public SshDockerEnvironment? Environment { get; private set; }
    /// <summary>操作筛选不参与远端命令。</summary>
    public string Search { get; set { if (SetProperty(ref field, value)) Operations.Refresh(); } } = "";
    /// <summary>选择操作时只重新生成预览，不自动执行。</summary>
    public SshDockerOperationItem? SelectedOperation { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } }
    /// <summary>容器名称只显示，命令以完整 ID 生成。</summary>
    public SshMonitorContainerItem? SelectedContainer { get; set { if (SetProperty(ref field, value)) { ShowDetails = false; RefreshPreview(); } } }
    /// <summary>限定日志行数。</summary>
    public int TailLines { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = 500;
    /// <summary>固定协议时间范围，与 UI 区域格式无关。</summary>
    public string Since { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = "1h";
    /// <summary>原样显示进程时间戳。</summary>
    public bool Timestamps { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = true;
    /// <summary>跟踪日志只在独立终端运行。</summary>
    public bool Follow { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } }
    /// <summary>优雅停止等待秒数，构建器限制一到三百秒。</summary>
    public string TimeoutSeconds { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = "10";
    /// <summary>用户填写的镜像引用，不允许 shell 语法。</summary>
    public string Image { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = "";
    /// <summary>显式远端绝对目录，不继承终端 cwd。</summary>
    public string ProjectDirectory { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = "";
    /// <summary>Compose 文件相对所选项目目录解释。</summary>
    public string ComposeFile { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = "compose.yaml";
    /// <summary>空服务名表示整个 Compose 项目。</summary>
    public string Service { get; set { if (SetProperty(ref field, value)) RefreshPreview(); } } = "";
    /// <summary>确认期间禁用其他入口和参数表单。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) Notify(); } }
    /// <summary>进度和取消状态为完整资源句式。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>安全错误，不展示 SDK 或失败命令的原始输出。</summary>
    public string Error { get; private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasError)); } } = "";
    /// <summary>参数校验或能力缺失提示。</summary>
    public string ValidationError { get; private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasValidationError)); } } = "";
    /// <summary>仅成功的显式进程输出保留原文，最多六十四 KiB。</summary>
    public string Output { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>上次执行的退出码、用时、截断和执行目标摘要。</summary>
    public string ResultSummary { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>结果绑定实际执行的命令，参数编辑后不会误标旧输出。</summary>
    public string ExecutedCommand { get; private set => SetProperty(ref field, value); } = "";

    /// <summary>首次打开自动探测，重复打开复用已识别环境。</summary>
    public Task InitializeAsync() => !IsInitialized ? DetectAsync(Environment is not null) : Task.CompletedTask;

    /// <summary>同一串行边界内选择动作并执行；忙碌时不改写当前计划。</summary>
    public async Task RunActionAsync(string id)
    {
        if (!CanRunAction(id)) return;
        DetailSection = "output";
        Select(id, SelectedContainer);
        await ExecuteAsync();
    }

    private bool CanRunAction(string? id) => !_disposed && !IsBusy && Environment is { CanAccess: true, SupportsBaseCommands: true } && _connection() is not null && id switch
    {
        "logs" => SelectedContainer is not null,
        "start" => SelectedContainer?.State is "exited" or "created" or "dead",
        "stop" or "restart" => SelectedContainer?.IsRunning == true,
        _ => false,
    };

    /// <summary>监测快捷入口带入当前容器与指定操作，不触发执行。</summary>
    public void Select(string id, SshMonitorContainerItem? container)
    {
        ToolSection = SshDockerCommandBuilder.Operations.First(o => o.Id == id).RequiresCompose ? "compose" : id is "images" or "pull" ? "images" : "containers";
        SelectedOperation = OtherOperations.FirstOrDefault(o => o.Id == id) ??
            SshDockerCommandBuilder.Operations.Select(o => new SshDockerOperationItem(o)).First(o => o.Id == id);
        SelectedOtherOperation = OtherOperations.FirstOrDefault(o => o.Id == id);
        if (container is not null)
        {
            if (!ContainerView.Cast<SshMonitorContainerItem>().Any(c => c.Id == container.Id)) { ContainerSearch = ""; ContainerState = ""; }
            if (!Containers.Any(c => c.Id == container.Id)) Containers.Add(container);
            SelectedContainer = Containers.First(c => c.Id == container.Id);
        }
    }

    private void RefreshPreview()
    {
        _plan = null; ValidationError = "";
        try
        {
            if (Environment is null) ValidationError = Text.Get("DockerDetectFirst");
            else if (SelectedOperation is { } operation)
            {
                if (operation.Definition.RequiresContainer && (SelectedContainer is null || !Containers.Any(c => c.Id == SelectedContainer.Id))) throw Text.Exception("SshMonitorSelectContainer");
                int timeout = int.TryParse(TimeoutSeconds, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
                _plan = SshDockerCommandBuilder.Build(operation.Id, new(SelectedContainer?.Id ?? "", TailLines, Since, Timestamps, Follow,
                    timeout, Image, ProjectDirectory, ComposeFile, Service), Environment);
            }
        }
        catch (Exception error) { ValidationError = Text.Error(error, "DockerInvalidParameters"); }
        Notify();
    }

    /// <summary>连接代次变更撤销确认和执行权限，清空旧主机的容器与版本。</summary>
    public void ConnectionChanged()
    {
        _generation++; _pending?.Cancel(); _pending = null; IsBusy = false; IsInitialized = false; Environment = null; SelectedContainer = null; Containers.Clear();
        Error = ""; Status = ""; Output = ""; ResultTarget = ""; ResultSummary = ""; ExecutedCommand = ""; RefreshPreview();
    }
    /// <inheritdoc />
    public void Dispose() { if (_disposed) return; _disposed = true; ConnectionChanged(); InsertRequested = null; Changed = null; }
}
