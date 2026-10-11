using System.Windows.Controls.Primitives;
using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Pages;

/// <summary>处理列表鼠标与键盘语义，业务操作统一委托页面命令。</summary>
public partial class ObjectStorageListView : UserControl
{
    /// <summary>窄视口隐藏次要列，业务模型仍保留完整元数据。</summary>
    public static readonly DependencyProperty StorageColumnWidthProperty = DependencyProperty.Register(nameof(StorageColumnWidth), typeof(GridLength), typeof(ObjectStorageListView), new PropertyMetadata(new GridLength(84)));
    /// <summary>更新时间列在足够宽的列表中显示。</summary>
    public static readonly DependencyProperty ModifiedColumnWidthProperty = DependencyProperty.Register(nameof(ModifiedColumnWidth), typeof(GridLength), typeof(ObjectStorageListView), new PropertyMetadata(new GridLength(134)));
    /// <summary>存储类型文字与对应列宽同步隐藏。</summary>
    public static readonly DependencyProperty StorageColumnVisibilityProperty = DependencyProperty.Register(nameof(StorageColumnVisibility), typeof(Visibility), typeof(ObjectStorageListView), new PropertyMetadata(Visibility.Visible));
    /// <summary>更新时间文字与对应列宽同步隐藏。</summary>
    public static readonly DependencyProperty ModifiedColumnVisibilityProperty = DependencyProperty.Register(nameof(ModifiedColumnVisibility), typeof(Visibility), typeof(ObjectStorageListView), new PropertyMetadata(Visibility.Visible));
    /// <summary>搜索框较窄时保留输入与搜索图标，不挤入说明及清空按钮。</summary>
    public static readonly DependencyProperty ShowSearchDecorationsProperty = DependencyProperty.Register(nameof(ShowSearchDecorations), typeof(bool), typeof(ObjectStorageListView), new PropertyMetadata(true));
    /// <summary>存储类型列的当前宽度。</summary>
    public GridLength StorageColumnWidth { get => (GridLength)GetValue(StorageColumnWidthProperty); private set => SetValue(StorageColumnWidthProperty, value); }
    /// <summary>更新时间列的当前宽度。</summary>
    public GridLength ModifiedColumnWidth { get => (GridLength)GetValue(ModifiedColumnWidthProperty); private set => SetValue(ModifiedColumnWidthProperty, value); }
    /// <summary>存储类型列的当前显示状态。</summary>
    public Visibility StorageColumnVisibility { get => (Visibility)GetValue(StorageColumnVisibilityProperty); private set => SetValue(StorageColumnVisibilityProperty, value); }
    /// <summary>更新时间列的当前显示状态。</summary>
    public Visibility ModifiedColumnVisibility { get => (Visibility)GetValue(ModifiedColumnVisibilityProperty); private set => SetValue(ModifiedColumnVisibilityProperty, value); }
    /// <summary>搜索提示与清空图标是否有足够空间显示。</summary>
    public bool ShowSearchDecorations { get => (bool)GetValue(ShowSearchDecorationsProperty); private set => SetValue(ShowSearchDecorationsProperty, value); }

    /// <summary>加载按设计稿拆分的对象表格。</summary>
    public ObjectStorageListView()
    {
        InitializeComponent();
        SizeChanged += OnListSizeChanged;
        SearchInput.SizeChanged += OnSearchSizeChanged;
    }

    /// <summary>保持设计尺寸的列宽，窄窗优先留出文件名与行操作空间。</summary>
    private void OnListSizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool storage = ActualWidth >= 520;
        bool modified = ActualWidth >= 650;
        StorageColumnWidth = new(storage ? 84 : 0);
        ModifiedColumnWidth = new(modified ? 134 : 0);
        StorageColumnVisibility = storage ? Visibility.Visible : Visibility.Collapsed;
        ModifiedColumnVisibility = modified ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>收窄时避免控件装饰覆盖用户输入，辅助技术仍可读取完整名称。</summary>
    private void OnSearchSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ShowSearchDecorations = SearchInput.ActualWidth >= 145;
        SearchInput.Padding = new Thickness(30, 0, ShowSearchDecorations ? 25 : 8, 0);
    }

    /// <summary>仅双击实际目录行进入前缀，按钮和勾选不触发导航。</summary>
    private async void OnObjectDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is not DependencyObject source || HasButtonAncestor(source)) return;
        if (ItemsControl.ContainerFromElement(ObjectRows, source) is ListBoxItem { DataContext: ObjectStorageEntryViewModel row }
            && DataContext is ObjectStorageWorkspaceViewModel model)
        {
            await model.OpenEntryAsync(row);
            args.Handled = row.IsDirectory;
        }
    }

    /// <summary>键盘 Enter 与目录双击一致。</summary>
    private async void OnRowKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && ObjectRows.SelectedItem is ObjectStorageEntryViewModel row && DataContext is ObjectStorageWorkspaceViewModel model)
        { await model.OpenEntryAsync(row); args.Handled = row.IsDirectory; }
    }

    /// <summary>右键菜单明确针对当前点击行，避免操作前一次选中的对象。</summary>
    private void OnRowRightClick(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ObjectRows, source) is ListBoxItem { DataContext: ObjectStorageEntryViewModel row } item)
        { ObjectRows.SelectedItem = row; ShowMenu(row, item); args.Handled = true; }
    }

    /// <summary>行末的更多按钮与右键菜单复用同一菜单。</summary>
    private void OnMoreClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: ObjectStorageEntryViewModel row } button)
        { ObjectRows.SelectedItem = row; ShowMenu(row, button); args.Handled = true; }
    }

    /// <summary>菜单命令使用明确的行参数，临时链接只对文件可用。</summary>
    private void ShowMenu(ObjectStorageEntryViewModel row, FrameworkElement target)
    {
        if (DataContext is not ObjectStorageWorkspaceViewModel model) return;
        var menu = new ContextMenu { PlacementTarget = target };
        MenuItem Item(string label, string icon, ICommand command)
        {
            var item = new MenuItem { Header = label, Command = command, CommandParameter = row, Icon = new IconView { Kind = icon, Width = 15, Height = 15 } };
            return item;
        }
        menu.Items.Add(Item("下载", "download", model.DownloadCommand));
        menu.Items.Add(Item("复制 Object Key", "copy", model.CopyKeyCommand));
        menu.Items.Add(Item("生成临时链接…", "link", model.TemporaryLinkCommand));
        menu.Items.Add(new Separator());
        var delete = Item("删除对象…", "trash-2", model.DeleteCommand);
        delete.SetResourceReference(ForegroundProperty, "DangerBrush");
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    /// <summary>勾选或操作按钮内的双击保留按钮本身行为。</summary>
    private static bool HasButtonAncestor(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null && current is not ListBoxItem; current = VisualTreeHelper.GetParent(current))
            if (current is ButtonBase) return true;
        return false;
    }
}
