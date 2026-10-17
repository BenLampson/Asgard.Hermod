using Asgard.Hermod.RabbitMq.UI.ViewModels;

namespace Asgard.Hermod.RabbitMq.UI.Views;

/// <summary>视图仅转发键盘、窗口活跃状态和助手入口，不持有协议请求。</summary>
public partial class RabbitMqWorkspaceView : UserControl
{
    private Window? _window;
    private bool _compact;
    public RabbitMqWorkspaceView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _window = Window.GetWindow(this); if (_window is not null) { _window.Activated += OnActivation; _window.Deactivated += OnActivation; } OnActivation(this, EventArgs.Empty); };
        Unloaded += (_, _) => { if (_window is not null) { _window.Activated -= OnActivation; _window.Deactivated -= OnActivation; } if (DataContext is RabbitMqWorkspaceViewModel model) model.IsActive = false; _window = null; };
        SizeChanged += (_, _) =>
        {
            bool compact = ActualWidth < 1200;
            if (DataContext is RabbitMqWorkspaceViewModel model)
            {
                if (compact != _compact) model.ExplorerOpen = !compact;
                model.ListTableMaximum = Math.Max(180, ActualHeight - 350);
            }
            _compact = compact;
        };
    }
    private void OnActivation(object? sender, EventArgs args) { if (DataContext is RabbitMqWorkspaceViewModel model) model.IsActive = _window?.IsActive ?? true; }
    private void OnNavigate(object sender, SelectionChangedEventArgs args)
    {
        if (DataContext is RabbitMqWorkspaceViewModel model && args.AddedItems.OfType<RabbitMqNavigation>().FirstOrDefault() is { } item && item.Resource != model.Resource)
            model.NavigateCommand.Execute(item);
    }
    private void OnOpenRow(object sender, MouseButtonEventArgs args)
    {
        if (DataContext is RabbitMqWorkspaceViewModel model && args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ResourceTable, source) is DataGridRow)
            model.OpenCommand.Execute(null);
    }
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (DataContext is not RabbitMqWorkspaceViewModel model) return;
        if (args.Key == Key.F5) { model.RefreshCommand.Execute(null); args.Handled = true; }
        if (args.Key == Key.Enter && ResourceTable.IsKeyboardFocusWithin) { model.OpenCommand.Execute(null); args.Handled = true; }
    }
    private void OnOpenAgent(object sender, RoutedEventArgs args)
    {
        if (DataContext is not RabbitMqWorkspaceViewModel model || TryFindResource(typeof(IAgentWindowService)) is not IAgentWindowService windows) return;
        var origin = Window.GetWindow(this);
        windows.Open(new(model, model.Assistant, Text.Format("RabbitMqAgentTitle", model.Instance.Name), model.Endpoint, "brand-rabbitmq",
            () => new RabbitMqAssistantView(model), () => { if (origin is not null && !model.IsClosed) { if (origin.WindowState == WindowState.Minimized) origin.WindowState = WindowState.Normal; origin.Activate(); } }, () => model.IsClosed));
    }
}
