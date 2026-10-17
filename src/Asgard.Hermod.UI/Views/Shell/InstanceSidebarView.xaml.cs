using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Views.Shell;

/// <summary>
/// 实例导航在完整列表和图标栏之间切换，共用筛选和命令绑定。
/// </summary>
public partial class InstanceSidebarView : UserControl
{
    private TagTreePickerViewModel? _tagDraft;
    private InstanceSidebarViewModel? _tagOwner;
    private Window? _popupWindow;

    /// <summary>
    /// 表示侧栏是否显示完整搜索框和实例名称。
    /// </summary>
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(InstanceSidebarView),
        new PropertyMetadata(true, OnIsExpandedChanged));

    /// <summary>
    /// 收起状态下请求展开并进入搜索的路由事件。
    /// </summary>
    public static readonly RoutedEvent ExpandSearchRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(ExpandSearchRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InstanceSidebarView));

    /// <summary>
    /// 创建实例侧栏。
    /// </summary>
    public InstanceSidebarView()
    {
        InitializeComponent();
        Unloaded += (_, _) => CloseTagPicker();
        DataContextChanged += (_, _) => CloseTagPicker();
    }

    /// <summary>
    /// 获取或设置展开状态，实际宽度由外壳布局决定。
    /// </summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// 让外壳完成展开操作，而不把 Shell 状态放入实例 ViewModel。
    /// </summary>
    public event RoutedEventHandler ExpandSearchRequested
    {
        add => AddHandler(ExpandSearchRequestedEvent, value);
        remove => RemoveHandler(ExpandSearchRequestedEvent, value);
    }

    /// <summary>
    /// 转移键盘焦点并选中现有查询，便于重新输入。
    /// </summary>
    public void FocusSearch()
    {
        InstanceSearch.Focus();
        InstanceSearch.SelectAll();
    }

    /// <summary>
    /// 图标栏搜索按钮只发送界面展开请求。
    /// </summary>
    private void OnCollapsedSearchClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ExpandSearchRequestedEvent, this));
    }

    /// <summary>
    /// 为本次弹出创建选择草稿，点击应用之前保留侧栏已有过滤条件。
    /// </summary>
    private void OnTagFilterClick(object sender, RoutedEventArgs e)
    {
        if (TagFilterPopup.IsOpen)
        {
            CloseTagPicker();
            return;
        }
        if (DataContext is not InstanceSidebarViewModel sidebar)
            return;
        _tagOwner = sidebar;
        _tagDraft = new TagTreePickerViewModel(sidebar.BuildTagFilterDefinitions(),
            sidebar.SelectedTagPaths, sidebar.TagMatchMode);
        _tagDraft.Completed += OnTagSelectionCompleted;
        TagPicker.Model = _tagDraft;
        TagPicker.Height = PopupWorkArea.GetMaximumHeight(TagFilterButton, 650);
        _popupWindow = Window.GetWindow(this);
        if (_popupWindow is not null)
        {
            _popupWindow.LocationChanged += OnOwnerBoundsChanged;
            _popupWindow.SizeChanged += OnOwnerSizeChanged;
            _popupWindow.Deactivated += OnOwnerDeactivated;
        }
        TagFilterPopup.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>外壳销毁、数据来源替换或侧栏收起时关闭仍然打开的草稿。</summary>
    public void CloseTagPicker() => TagFilterPopup.IsOpen = false;

    /// <summary>打开后由组件聚焦搜索框，键盘输入不落到实例搜索中。</summary>
    private void OnTagPopupOpened(object sender, EventArgs e) => TagPicker.FocusSearch();

    /// <summary>仅应用事件能修改侧栏；关闭、Escape 和外部点击只放弃草稿。</summary>
    private void OnTagSelectionCompleted(object? sender, bool applied)
    {
        if (applied && _tagDraft is not null && _tagOwner is not null)
            _tagOwner.ApplyTagFilter(_tagDraft.SelectedPaths, _tagDraft.MatchMode);
        CloseTagPicker();
    }

    /// <summary>释放窗口与草稿订阅，让重新打开的选择器读取最新已应用状态。</summary>
    private void OnTagPopupClosed(object sender, EventArgs e)
    {
        bool restoreFocus = TagPicker.IsKeyboardFocusWithin;
        if (_tagDraft is not null)
            _tagDraft.Completed -= OnTagSelectionCompleted;
        if (_popupWindow is not null)
        {
            _popupWindow.LocationChanged -= OnOwnerBoundsChanged;
            _popupWindow.SizeChanged -= OnOwnerSizeChanged;
            _popupWindow.Deactivated -= OnOwnerDeactivated;
        }
        _tagDraft = null;
        _tagOwner = null;
        _popupWindow = null;
        TagPicker.Model = null;
        if (restoreFocus && IsVisible && IsExpanded && Window.GetWindow(this)?.IsActive == true)
            TagFilterButton.Focus();
    }

    /// <summary>避免独立 Popup 在所属窗口移动后留在原来的屏幕坐标。</summary>
    private void OnOwnerBoundsChanged(object? sender, EventArgs e) => CloseTagPicker();

    /// <summary>窗口缩放改变可用空间后，下次打开重新计算选择器高度。</summary>
    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs e) => CloseTagPicker();

    /// <summary>应用失去焦点时关闭菜单，防止它浮在其他应用之上。</summary>
    private void OnOwnerDeactivated(object? sender, EventArgs e) => CloseTagPicker();

    /// <summary>收起导航之前移除浮层，避免不可见锚点仍占用鼠标捕获。</summary>
    private static void OnIsExpandedChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
            ((InstanceSidebarView)target).CloseTagPicker();
    }
}
