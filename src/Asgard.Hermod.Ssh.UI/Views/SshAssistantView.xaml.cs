using System.Collections.Specialized;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>消息到达只滚动对话，不打开分屏或抢占终端焦点。</summary>
public partial class SshAssistantView : UserControl
{
    private SshAssistantViewModel? _observed;
    /// <summary>要求宿主隐藏分屏，不关闭或清空对话。</summary>
    public event EventHandler? CloseRequested;
    /// <summary>用户明确附加终端选区。</summary>
    public event EventHandler? AttachSelectionRequested;
    /// <summary>加载主题化助手布局。</summary>
    public SshAssistantView()
    {
        InitializeComponent();
        Loaded += (_, _) => ObserveMessages();
        Unloaded += (_, _) => { if (_observed is not null) _observed.Messages.CollectionChanged -= OnMessagesChanged; _observed = null; };
        DataContextChanged += (_, _) => { if (IsLoaded) ObserveMessages(); };
    }
    private void ObserveMessages()
    {
        if (_observed is not null) _observed.Messages.CollectionChanged -= OnMessagesChanged;
        _observed = DataContext as SshAssistantViewModel;
        if (_observed is not null) _observed.Messages.CollectionChanged += OnMessagesChanged;
    }
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (IsVisible) Dispatcher.BeginInvoke(() => { if (IsVisible) ConversationScroll.ScrollToEnd(); });
    }
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnAttachSelection(object sender, RoutedEventArgs args) => AttachSelectionRequested?.Invoke(this, EventArgs.Empty);
    private void OnPromptKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || !PromptEditor.IsKeyboardFocusWithin) return;
        if (DataContext is SshAssistantViewModel model && model.SendCommand.CanExecute(null)) { model.SendCommand.Execute(null); args.Handled = true; }
    }
}
