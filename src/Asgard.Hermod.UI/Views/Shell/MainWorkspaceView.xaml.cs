using System.Windows.Controls.Primitives;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Views.Shell;

/// <summary>按设计稿组合实例表格，选择、打开、菜单和标签草稿分别处理。</summary>
public partial class MainWorkspaceView : UserControl
{
    private TagTreePickerViewModel? _tagDraft;
    /// <summary>状态及真实数据均由工作台模型提供。</summary>
    public MainWorkspaceView()
    {
        InitializeComponent();
        Unloaded += (_, _) => CloseTagPicker();
        DataContextChanged += (_, args) =>
        {
            CloseTagPicker();
            if (args.OldValue is MainWorkspaceViewModel previous) previous.PropertyChanged -= OnModelChanged;
            if (args.NewValue is MainWorkspaceViewModel next) next.PropertyChanged += OnModelChanged;
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainWorkspaceViewModel.SelectedNavigation)) CloseTagPicker();
    }
    /// <summary>Ctrl+F 从窗口标题栏或正文统一进入筛选，不打开连接。</summary>
    public void FocusSearch()
    {
        if (DataContext is not MainWorkspaceViewModel model) return;
        if (model.IsOverview) model.SelectedNavigation = model.Navigation[1];
        _ = Dispatcher.BeginInvoke(() => { SearchInput.Focus(); SearchInput.SelectAll(); });
    }
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is not DependencyObject source || FindButton(source) is not null) return;
        if (ItemsControl.ContainerFromElement(InstanceRows, source) is ListBoxItem { DataContext: MainInstanceRow row }
            && DataContext is MainWorkspaceViewModel model) model.OpenCommand.Execute(row);
    }
    private static Button? FindButton(DependencyObject? source)
    {
        while (source is not null && source is not ListBoxItem)
        {
            if (source is Button button) return button;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }
    private void OnListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && DataContext is MainWorkspaceViewModel { SelectedRow: { } row } model)
        { model.OpenCommand.Execute(row); args.Handled = true; }
    }
    private void OnMoreClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: MainInstanceRow row, ContextMenu: { } menu } button) return;
        if (DataContext is MainWorkspaceViewModel model) model.SelectedRow = row;
        menu.PlacementTarget = button; menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popup, target, _) => [new(new Point(target.Width - popup.Width, target.Height + 4), PopupPrimaryAxis.Vertical)];
        menu.IsOpen = true; args.Handled = true;
    }
    private void OnMenuClick(object sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { DataContext: MainInstanceRow row, Tag: string action } || DataContext is not MainWorkspaceViewModel model) return;
        ICommand command = action switch { "open" => model.OpenCommand, "rename" => model.RenameCommand, _ => model.EditCommand };
        if (command.CanExecute(row)) command.Execute(row);
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (DataContext is MainWorkspaceViewModel model) model.SetListWidth(ActualWidth);
        if (SearchArea.Parent is Grid grid) grid.ColumnDefinitions[0].Width = new GridLength(ActualWidth < 1150 ? 270 : 370);
    }
    private void OnTableSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (DataContext is MainWorkspaceViewModel model) model.SetListHeight(InstanceTable.ActualHeight);
    }
    private void OnTagFilterClick(object sender, RoutedEventArgs args)
    {
        if (TagFilterPopup.IsOpen) { CloseTagPicker(); return; }
        if (DataContext is not MainWorkspaceViewModel model) return;
        _tagDraft = new(model.Catalog.BuildTagFilterDefinitions(), model.Catalog.SelectedTagPaths, model.Catalog.TagMatchMode);
        _tagDraft.Completed += OnTagsCompleted; TagPicker.Model = _tagDraft;
        TagPicker.Height = PopupWorkArea.GetMaximumHeight(TagFilterButton, 650); TagFilterPopup.IsOpen = true;
    }
    private void OnTagsCompleted(object? sender, bool applied)
    {
        if (applied && _tagDraft is not null && DataContext is MainWorkspaceViewModel model)
            model.Catalog.ApplyTagFilter(_tagDraft.SelectedPaths, _tagDraft.MatchMode);
        CloseTagPicker();
    }
    /// <summary>取消、离开主页面和退出时均丢弃未应用的筛选草稿。</summary>
    public void CloseTagPicker() => TagFilterPopup.IsOpen = false;
    private void OnTagPopupOpened(object? sender, EventArgs args) => TagPicker.FocusSearch();
    private void OnTagPopupClosed(object? sender, EventArgs args)
    {
        if (_tagDraft is not null) _tagDraft.Completed -= OnTagsCompleted;
        _tagDraft = null; TagPicker.Model = null;
    }
}
