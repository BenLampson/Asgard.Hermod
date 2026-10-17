using System.Windows.Input;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.Controls;

/// <summary>来源独立的模型选择器；忙碌切换先呈现内嵌确认，再等待任务退出。</summary>
public partial class AgentModelSelector : UserControl
{
    /// <summary>来源的模型选择。</summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(nameof(Session), typeof(AgentModelSession), typeof(AgentModelSelector), new PropertyMetadata(null, OnSessionChanged));
    /// <summary>包括上下文读取、确认和远端执行的来源忙碌状态。</summary>
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(AgentModelSelector), new PropertyMetadata(false, OnBusyChanged));
    /// <summary>来源拥有的取消命令，不由选择控件直接操作远端。</summary>
    public static readonly DependencyProperty CancelTaskProperty = DependencyProperty.Register(nameof(CancelTask), typeof(ICommand), typeof(AgentModelSelector));
    /// <summary>真实的模型名称。</summary>
    public static readonly DependencyProperty ProfileNameProperty = DependencyProperty.Register(nameof(ProfileName), typeof(string), typeof(AgentModelSelector), new PropertyMetadata(""));
    /// <summary>真实模型标识。</summary>
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(string), typeof(AgentModelSelector), new PropertyMetadata(""));
    /// <summary>当前配置摘要。</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(AgentModelSelector), new PropertyMetadata(""));
    /// <summary>等待用户确认切换。</summary>
    public static readonly DependencyProperty NeedsConfirmationProperty = DependencyProperty.Register(nameof(NeedsConfirmation), typeof(bool), typeof(AgentModelSelector));
    /// <summary>完整切换影响提示。</summary>
    public static readonly DependencyProperty SwitchPromptProperty = DependencyProperty.Register(nameof(SwitchPrompt), typeof(string), typeof(AgentModelSelector), new PropertyMetadata(""));
    /// <summary>安全错误。</summary>
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(nameof(Error), typeof(string), typeof(AgentModelSelector), new PropertyMetadata(""));
    /// <summary>有目录时反映真实可请求状态，兼容无目录的替代传输。</summary>
    public static readonly DependencyProperty IsConfiguredProperty = DependencyProperty.Register(nameof(IsConfigured), typeof(bool), typeof(AgentModelSelector), new PropertyMetadata(true));
    private AgentModelRow? _pending;
    private TaskCompletionSource? _idle;
    /// <summary>模型会话。</summary>
    public AgentModelSession? Session { get => (AgentModelSession?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    /// <summary>来源忙碌状态。</summary>
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    /// <summary>来源取消命令。</summary>
    public ICommand? CancelTask { get => (ICommand?)GetValue(CancelTaskProperty); set => SetValue(CancelTaskProperty, value); }
    /// <summary>配置名称。</summary>
    public string ProfileName { get => (string)GetValue(ProfileNameProperty); private set => SetValue(ProfileNameProperty, value); }
    /// <summary>模型标识。</summary>
    public string Model { get => (string)GetValue(ModelProperty); private set => SetValue(ModelProperty, value); }
    /// <summary>摘要。</summary>
    public string Label { get => (string)GetValue(LabelProperty); private set => SetValue(LabelProperty, value); }
    /// <summary>确认可见性。</summary>
    public bool NeedsConfirmation { get => (bool)GetValue(NeedsConfirmationProperty); private set { SetValue(NeedsConfirmationProperty, value); ConfirmSwitchCommand?.NotifyCanExecuteChanged(); } }
    /// <summary>确认提示。</summary>
    public string SwitchPrompt { get => (string)GetValue(SwitchPromptProperty); private set => SetValue(SwitchPromptProperty, value); }
    /// <summary>错误提示。</summary>
    public string Error { get => (string)GetValue(ErrorProperty); private set => SetValue(ErrorProperty, value); }
    /// <summary>当前模型是否可用。</summary>
    public bool IsConfigured { get => (bool)GetValue(IsConfiguredProperty); private set => SetValue(IsConfiguredProperty, value); }
    /// <summary>不包含密钥的选项。</summary>
    public ObservableCollection<AgentModelRow> Choices { get; } = [];
    /// <summary>选择配置。</summary>
    public RelayCommand SelectCommand { get; }
    /// <summary>明确停止并切换。</summary>
    public AsyncRelayCommand ConfirmSwitchCommand { get; }
    /// <summary>取消未执行的切换。</summary>
    public RelayCommand CancelSwitchCommand { get; }
    /// <summary>打开现有模型设置。</summary>
    public RelayCommand ManageCommand { get; }
    /// <summary>交还宿主设置入口。</summary>
    public event EventHandler? ManageRequested;
    /// <summary>构造纯控件，不打开原生窗口。</summary>
    public AgentModelSelector()
    {
        SelectCommand = new RelayCommand(value => { if (value is AgentModelRow row) Select(row); });
        ConfirmSwitchCommand = new AsyncRelayCommand(_ => ConfirmAsync(), _ => NeedsConfirmation);
        CancelSwitchCommand = new RelayCommand(_ => { _pending = null; NeedsConfirmation = false; }, _ => !ConfirmSwitchCommand.IsRunning);
        ConfirmSwitchCommand.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AsyncRelayCommand.IsRunning)) CancelSwitchCommand.NotifyCanExecuteChanged(); };
        ManageCommand = new RelayCommand(_ => { ChoicesPopup.IsOpen = false; ManageRequested?.Invoke(this, EventArgs.Empty); });
        InitializeComponent(); Refresh();
    }
    private static void OnBusyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is false) ((AgentModelSelector)sender)._idle?.TrySetResult();
    }
    private static void OnSessionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (AgentModelSelector)sender;
        if (args.OldValue is AgentModelSession old) WeakEventManager<AgentModelSession, EventArgs>.RemoveHandler(old, nameof(old.Changed), control.OnChanged);
        if (args.NewValue is AgentModelSession current) WeakEventManager<AgentModelSession, EventArgs>.AddHandler(current, nameof(current.Changed), control.OnChanged);
        control.Refresh();
    }
    private void OnChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Refresh()); return; }
        Refresh();
    }
    private void Refresh()
    {
        ProfileName = Session?.Name ?? Text.Get("ChatConfigureModel"); Model = Session?.Model ?? ""; Label = Session?.Label ?? ProfileName;
        IsConfigured = Session?.IsConfigured ?? true;
        Choices.Clear(); foreach (var profile in Session?.Profiles ?? []) Choices.Add(new(profile, "", Session?.SelectedId ?? ""));
        if (_pending is { } pending && !Choices.Any(row => row.Id == pending.Id)) { _pending = null; NeedsConfirmation = false; }
    }
    private void OnOpen(object sender, RoutedEventArgs args)
    {
        if (Session is null || Choices.Count == 0) { ManageRequested?.Invoke(this, EventArgs.Empty); return; }
        ChoicesPopup.IsOpen = true;
    }
    private void Select(AgentModelRow row)
    {
        ChoicesPopup.IsOpen = false; Error = "";
        if (row.Id == Session?.SelectedId) return;
        if (IsBusy || Session?.IsBusy == true) { _pending = row; SwitchPrompt = Text.Format("AgentModelSwitchPrompt", row.Name); NeedsConfirmation = true; return; }
        try { Session?.Select(row.Id); }
        catch (Exception error) { Error = Text.Error(error, "AgentRequestFailed"); }
    }
    private async Task ConfirmAsync()
    {
        if (_pending is not { } pending || Session is not { } session) return;
        Error = "";
        try
        {
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (IsBusy)
            {
                if (CancelTask?.CanExecute(null) != true) throw Text.Exception("AgentModelStopBeforeSwitch");
                CancelTask.Execute(null);
                if (IsBusy) await _idle.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            await session.StopAndSelectAsync(pending.Id);
            _pending = null; NeedsConfirmation = false;
        }
        catch (Exception error) { Error = Text.Error(error, "AgentModelStopBeforeSwitch"); }
        finally { _idle = null; }
    }
}
