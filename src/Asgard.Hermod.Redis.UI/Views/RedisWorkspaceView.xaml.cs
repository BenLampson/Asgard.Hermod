using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Views;

/// <summary>仅负责原生键盘导航、紧凑布局及视图缓存，不持有协议数据。</summary>
public partial class RedisWorkspaceView : UserControl
{
    private readonly Dictionary<RedisKeyTabViewModel, RedisKeyContentView> _views = [];
    private RedisWorkspaceViewModel? _model;
    private bool _compact;
    private double _treeWidth = 280;
    private readonly System.Windows.Threading.DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    /// <summary>内部页签不创建外层工作页，切换时保留编辑器滚动位置。</summary>
    public RedisWorkspaceView()
    {
        InitializeComponent();
        CompactSelector.ItemsSource = new[] { new InstanceFieldChoice("tree", Text.Get("RedisKeys")), new InstanceFieldChoice("key", Text.Get("RedisValue")) };
        CompactSelector.SelectedValue = "tree";
        _searchDelay.Tick += async (_, _) => { _searchDelay.Stop(); if (_model?.IsConnected == true) await _model.QueryAsync(); };
        DataContextChanged += (_, _) => Attach(); Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { _searchDelay.Stop(); if (_model is not null) _model.PropertyChanged -= OnModelChanged; _model = null; };
    }
    private void Attach()
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = DataContext as RedisWorkspaceViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelChanged; ShowActive();
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RedisWorkspaceViewModel.ActiveTab)) ShowActive();
        if (args.PropertyName == nameof(RedisWorkspaceViewModel.ShowTree)) UpdateLayoutMode();
    }
    private void ShowActive()
    {
        if (_model is null) return;
        foreach (var removed in _views.Keys.Where(tab => !_model.Tabs.Contains(tab)).ToArray()) _views.Remove(removed);
        if (_model.ActiveTab is not { } active) { ContentHost.Content = null; return; }
        if (!_views.TryGetValue(active, out var view)) { view = new() { DataContext = active }; _views.Add(active, view); }
        ContentHost.Content = view; KeyTabs.ScrollIntoView(active); UpdateLayoutMode();
    }
    private async void OnTreeSelection(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (_model is not null) _model.CreatePrefix = args.NewValue is RedisTreeNode { IsGroup: true } group ? group.Prefix : "";
        if (_model is not null && args.NewValue is RedisTreeNode { Key: { } key }) await _model.OpenAsync(key);
    }
    private async void OnTreeKeyDown(object sender, KeyEventArgs args)
    {
        if (_model is null || args.Key != Key.Enter || KeyTree.SelectedItem is not RedisTreeNode node) return;
        args.Handled = true;
        if (node.Key is { } key) await _model.OpenAsync(key); else node.IsExpanded = !node.IsExpanded;
    }
    private void OnTreeClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ClickCount != 1) return;
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source is not null; source = System.Windows.Media.VisualTreeHelper.GetParent(source))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return;
            if (source is TreeViewItem { DataContext: RedisTreeNode { IsGroup: true } group }) { group.IsExpanded = !group.IsExpanded; args.Handled = true; return; }
        }
    }
    private async void OnTypeFilterChanged(object sender, SelectionChangedEventArgs args)
    { if (IsLoaded && _model?.IsConnected == true) await _model.QueryAsync(); }
    private async void OnDatabaseChanged(object sender, SelectionChangedEventArgs args)
    { if (IsLoaded && _model is { IsConnected: true, IsConnecting: false }) await _model.QueryAsync(); }
    private async void OnDatabasesOpened(object? sender, EventArgs args)
    { if (_model is { IsConnected: true }) await _model.RefreshDatabaseCountsAsync(); }
    private void OnSearchChanged(object sender, TextChangedEventArgs args)
    { if (!IsLoaded || _model?.IsConnected != true) return; _searchDelay.Stop(); _searchDelay.Start(); }
    private void OnCollapseGroups(object sender, RoutedEventArgs args) => _model?.Tree?.CollapseAll();
    private void OnTreeMenu(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu();
        var help = new MenuItem { Header = Text.Get("RedisKeySearchHelp") };
        help.Click += (_, _) => new Asgard.Hermod.UI.Views.Dialogs.MessageDialog(Text.Get("RedisKeySearchHelp"), Text.Get("RedisTreeHint"))
        { Owner = Window.GetWindow(this) }.ShowDialog();
        var open = new MenuItem { Header = Text.Get("RedisOpenKey") }; open.Click += OnOpenKnownKey;
        menu.Items.Add(help); menu.Items.Add(open); menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
    }
    private async void OnOpenKnownKey(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        var dialog = new HermodDialog { Owner = Window.GetWindow(this), Title = Text.Get("RedisOpenKey"), Width = 540, Height = 290 };
        var input = new TextBox { MaxLength = 4096 };
        var format = new ComboBox
        {
            ItemsSource = new[] { new InstanceFieldChoice("text", Text.Get("RedisText")), new InstanceFieldChoice("base64", Text.Get("RedisBase64")) },
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            SelectedValue = "text",
            Margin = new Thickness(0, 0, 0, 12)
        };
        var field = new FormField { Label = Text.Get("RedisKnownKey"), Content = input, IsRequired = true };
        var content = new StackPanel { Margin = new Thickness(24) }; content.Children.Add(format); content.Children.Add(field); dialog.Content = content;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(new Button { Content = Text.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        RedisKeyId? key = null;
        var accept = new Button { Content = Text.Get("RedisOpenKey") }; accept.SetResourceReference(StyleProperty, "PrimaryButton");
        accept.Click += (_, _) =>
        {
            try
            {
                if (input.Text.Length == 0) { field.Error = Text.Get("RedisDraftInvalid"); return; }
                key = format.SelectedValue as string == "base64" ? new(RedisValueCodec.Decode(input.Text, "base64")) : RedisKeyId.FromText(input.Text);
                dialog.DialogResult = true;
            }
            catch { field.Error = Text.Get("RedisInvalidFormat"); }
        };
        buttons.Children.Add(accept); dialog.FooterContent = new DialogActionBar { Content = buttons }; dialog.Loaded += (_, _) => input.Focus();
        if (dialog.ShowDialog() == true && key is not null) await _model.OpenAsync(key);
    }
    private void OnTabSelected(object sender, SelectionChangedEventArgs args) => ShowActive();
    private async void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (_model is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0 || !KeyPanel.IsKeyboardFocusWithin) return;
        if (args.Key == Key.S) { args.Handled = true; if (_model.ActiveTab is { } tab) await tab.SaveCommand.ExecuteAsync(); }
        else if (args.Key == Key.W) { args.Handled = true; await _model.CloseTabCommand.ExecuteAsync(_model.ActiveTab); }
        else if (args.Key == Key.Tab) { args.Handled = true; _model.CycleTab((Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : -1); }
    }
    private void OnOpenTabsMenu(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        var menu = new ContextMenu();
        foreach (var tab in _model.Tabs) menu.Items.Add(new MenuItem
        {
            Header = tab.MenuLabel,
            ToolTip = tab.MenuLabel,
            Command = _model.ActivateTabCommand,
            CommandParameter = tab,
            IsCheckable = false
        });
        menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
    }
    private void OnConnectionMenu(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = Text.Get("RedisEditConnection"), Command = _model.EditConnectionCommand });
        menu.Items.Add(new MenuItem { Header = Text.Get("RedisReconnect"), Command = _model.ReconnectCommand });
        menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
    }
    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool compact = args.NewSize.Width < 760;
        if (!_compact && compact) _treeWidth = TreeColumn.ActualWidth;
        _compact = compact; UpdateLayoutMode();
        ConnectionBadge.Visibility = args.NewSize.Width < 760 ? Visibility.Collapsed : Visibility.Visible;
        ToolbarTypeIcon.Visibility = args.NewSize.Width < 760 ? Visibility.Collapsed : Visibility.Visible;
        DatabaseField.Width = args.NewSize.Width < 600 ? 150 : 190;
        NewKeyLabel.Visibility = args.NewSize.Width < 600 ? Visibility.Collapsed : Visibility.Visible;
        IdentityColumn.Width = new(args.NewSize.Width < 600 ? 0 : args.NewSize.Width < 760 ? 70 : 225);
    }
    private void OnCompactChanged(object sender, SelectionChangedEventArgs args)
    { if (_model is not null) _model.ShowTree = CompactSelector.SelectedValue as string == "tree"; }
    private void UpdateLayoutMode()
    {
        CompactSelector.Visibility = _compact ? Visibility.Visible : Visibility.Collapsed;
        bool tree = _model?.ShowTree ?? true;
        TreeColumn.MinWidth = _compact ? 0 : 220; KeyColumn.MinWidth = _compact ? 0 : 420;
        TreeColumn.Width = _compact ? tree ? new(1, GridUnitType.Star) : new(0) : new(Math.Max(220, _treeWidth));
        KeyColumn.Width = _compact && tree ? new(0) : new(1, GridUnitType.Star);
        SplitterColumn.Width = new(_compact ? 0 : 6); Splitter.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        TreePanel.Visibility = _compact && !tree ? Visibility.Collapsed : Visibility.Visible;
        KeyPanel.Visibility = _compact && tree ? Visibility.Collapsed : Visibility.Visible;
        if (_compact) CompactSelector.SelectedValue = tree ? "tree" : "key";
    }
}
