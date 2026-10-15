using System.Collections;
using System.Collections.Specialized;

namespace Asgard.Hermod.UI.Controls;

/// <summary>协议无关的聊天面板；仅管理布局、输入与滚动，所有业务操作交还模块命令。</summary>
public partial class AgentPanel : UserControl
{
    /// <summary>模块保留所有权的消息集合。</summary>
    public static readonly DependencyProperty MessagesProperty = DependencyProperty.Register(nameof(Messages), typeof(IEnumerable), typeof(AgentPanel), new PropertyMetadata(null, OnMessagesChanged));
    /// <summary>模块声明消息和业务建议的绑定，外观由共享消息控件提供。</summary>
    public static readonly DependencyProperty MessageTemplateProperty = DependencyProperty.Register(nameof(MessageTemplate), typeof(DataTemplate), typeof(AgentPanel));
    /// <summary>模块上下文选项插槽。</summary>
    public static readonly DependencyProperty ContextContentProperty = DependencyProperty.Register(nameof(ContextContent), typeof(object), typeof(AgentPanel));
    /// <summary>双向草稿，隐藏面板时不会清空。</summary>
    public static readonly DependencyProperty PromptProperty = DependencyProperty.Register(nameof(Prompt), typeof(string), typeof(AgentPanel), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>模块提供的本地化输入提示。</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(AgentPanel), new PropertyMetadata(""));
    /// <summary>保留各协议既有的输入上限。</summary>
    public static readonly DependencyProperty MaxPromptLengthProperty = DependencyProperty.Register(nameof(MaxPromptLength), typeof(int), typeof(AgentPanel), new PropertyMetadata(16000));
    /// <summary>发送命令，启用条件仍由模块决定。</summary>
    public static readonly DependencyProperty SendCommandProperty = DependencyProperty.Register(nameof(SendCommand), typeof(ICommand), typeof(AgentPanel));
    /// <summary>取消当前任务，不由展示层直接取消连接或 SQL。</summary>
    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(nameof(CancelCommand), typeof(ICommand), typeof(AgentPanel));
    /// <summary>新建对话命令，保留模块既有的确认步骤。</summary>
    public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(nameof(ClearCommand), typeof(ICommand), typeof(AgentPanel));
    /// <summary>可选的收起命令，未提供时由宿主处理收起事件。</summary>
    public static readonly DependencyProperty HideCommandProperty = DependencyProperty.Register(nameof(HideCommand), typeof(ICommand), typeof(AgentPanel));
    /// <summary>模块任务状态控制停止按钮与进度提示。</summary>
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(AgentPanel));
    /// <summary>已经由模块安全格式化的错误文本。</summary>
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(nameof(Error), typeof(string), typeof(AgentPanel), new PropertyMetadata(""));
    /// <summary>模块提供的本地化任务进度。</summary>
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(string), typeof(AgentPanel), new PropertyMetadata(""));
    private INotifyCollectionChanged? _observed;

    /// <summary>收起请求不结束任务、不清除消息，也不销毁业务会话。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>展示期间订阅消息，卸载时解除订阅，不通过消息更新抢占焦点。</summary>
    public AgentPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => ObserveMessages();
        Unloaded += (_, _) => DetachMessages();
    }

    /// <summary>当前消息。</summary>
    public IEnumerable? Messages { get => (IEnumerable?)GetValue(MessagesProperty); set => SetValue(MessagesProperty, value); }
    /// <summary>当前消息模板。</summary>
    public DataTemplate? MessageTemplate { get => (DataTemplate?)GetValue(MessageTemplateProperty); set => SetValue(MessageTemplateProperty, value); }
    /// <summary>上下文插槽内容。</summary>
    public object? ContextContent { get => GetValue(ContextContentProperty); set => SetValue(ContextContentProperty, value); }
    /// <summary>双向输入草稿。</summary>
    public string Prompt { get => (string)GetValue(PromptProperty); set => SetValue(PromptProperty, value); }
    /// <summary>输入提示与无障碍名称。</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    /// <summary>输入字符上限。</summary>
    public int MaxPromptLength { get => (int)GetValue(MaxPromptLengthProperty); set => SetValue(MaxPromptLengthProperty, value); }
    /// <summary>发送入口。</summary>
    public ICommand? SendCommand { get => (ICommand?)GetValue(SendCommandProperty); set => SetValue(SendCommandProperty, value); }
    /// <summary>停止入口。</summary>
    public ICommand? CancelCommand { get => (ICommand?)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    /// <summary>新对话入口。</summary>
    public ICommand? ClearCommand { get => (ICommand?)GetValue(ClearCommandProperty); set => SetValue(ClearCommandProperty, value); }
    /// <summary>收起入口。</summary>
    public ICommand? HideCommand { get => (ICommand?)GetValue(HideCommandProperty); set => SetValue(HideCommandProperty, value); }
    /// <summary>任务是否正在运行。</summary>
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    /// <summary>安全错误。</summary>
    public string Error { get => (string)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    /// <summary>任务进度。</summary>
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// <summary>切换页签时立即解除旧集合，避免继续观察其他页签的消息。</summary>
    private static void OnMessagesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (AgentPanel)sender;
        if (panel.IsLoaded) panel.ObserveMessages();
    }

    /// <summary>只观察展示生命周期内的当前消息集合。</summary>
    private void ObserveMessages()
    {
        DetachMessages();
        _observed = Messages as INotifyCollectionChanged;
        if (_observed is not null) _observed.CollectionChanged += OnCollectionChanged;
    }

    /// <summary>移除事件引用，允许旧业务模型被正常回收。</summary>
    private void DetachMessages()
    {
        if (_observed is not null) _observed.CollectionChanged -= OnCollectionChanged;
        _observed = null;
    }

    /// <summary>新消息到达只滚动当前会话，不打开隐藏面板或改变键盘焦点。</summary>
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (IsVisible) Dispatcher.BeginInvoke(() => { if (IsLoaded && IsVisible) ConversationScroll.ScrollToEnd(); });
    }

    /// <summary>转发收起请求，真正的窗口和业务生命周期由模块处理。</summary>
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>统一 Enter 发送、Shift+Enter 换行，并兼容原 SQL 的 Ctrl+Enter；只处理输入框中的按键。</summary>
    private void OnPromptKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Control) || !PromptEditor.IsKeyboardFocusWithin) return;
        args.Handled = true;
        // 已禁用的发送入口也消费 Enter，避免忙碌时意外向草稿插入换行。
        if (SendCommand?.CanExecute(null) == true) SendCommand.Execute(null);
    }
}
