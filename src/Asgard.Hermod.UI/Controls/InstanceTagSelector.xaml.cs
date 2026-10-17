using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Controls;

/// <summary>确认页的弹出式标签多选，窗口移动、失焦与步骤切换都会取消未确认草稿。</summary>
public partial class InstanceTagSelector : UserControl
{
    private InstanceTagPickerViewModel? _draft;
    private Window? _owner;

    /// <summary>绑定向导独立的标签选择状态，不依赖具体实例类型。</summary>
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model),
        typeof(InstanceTagSelectionViewModel), typeof(InstanceTagSelector), new PropertyMetadata(null, OnModelChanged));
    /// <summary>当前确认页的已应用选择。</summary>
    public InstanceTagSelectionViewModel? Model
    {
        get => (InstanceTagSelectionViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
    /// <summary>加载本地化布局与弹出焦点作用域。</summary>
    public InstanceTagSelector() => InitializeComponent();

    /// <summary>仅在确认页可编辑状态创建草稿，重复点击入口取消上次操作。</summary>
    private void OnOpenClick(object sender, RoutedEventArgs args)
    {
        if (TagPopup.IsOpen) { ClosePicker(); return; }
        _draft = Model?.OpenPicker();
        if (_draft is null) return;
        _draft.Completed += OnCompleted;
        PopupPanel.DataContext = _draft;
        PopupPanel.Width = Math.Min(420, Math.Max(240, TagTrigger.ActualWidth));
        PopupPanel.Height = PopupWorkArea.GetMaximumHeight(TagTrigger, 420);
        _owner = Window.GetWindow(this);
        if (_owner is not null)
        {
            _owner.LocationChanged += OnOwnerChanged;
            _owner.SizeChanged += OnOwnerSizeChanged;
            _owner.Deactivated += OnOwnerChanged;
        }
        TagPopup.IsOpen = true;
        args.Handled = true;
    }

    /// <summary>面板打开后进入搜索框，回车不会直接保存实例。</summary>
    private void OnPopupOpened(object sender, EventArgs args) => TagSearch.Focus();
    /// <summary>确认和取消均关闭面板，已应用状态由模型独立处理。</summary>
    private void OnCompleted(object? sender, bool applied) => ClosePicker();

    /// <summary>外部点击只取消草稿，释放窗口订阅和输入焦点。</summary>
    private void OnPopupClosed(object sender, EventArgs args)
    {
        if (_draft is not null)
        {
            _draft.Completed -= OnCompleted;
            _draft.CancelCommand.Execute(null);
        }
        if (_owner is not null)
        {
            _owner.LocationChanged -= OnOwnerChanged;
            _owner.SizeChanged -= OnOwnerSizeChanged;
            _owner.Deactivated -= OnOwnerChanged;
        }
        _draft = null;
        _owner = null;
        PopupPanel.DataContext = null;
        if (IsVisible && Model?.IsEditable == true && Window.GetWindow(this)?.IsActive == true) TagTrigger.Focus();
    }

    /// <summary>Escape 取消，方向键浏览和展开标签；复选框原生处理 Space。</summary>
    private void OnPopupKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            _draft?.CancelCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.Enter && TagSearch.IsKeyboardFocusWithin) args.Handled = true;
        else if (args.OriginalSource is FrameworkElement { DataContext: InstanceTagOptionViewModel option })
        {
            if (args.Key is Key.Left or Key.Right && option.HasChildren)
            {
                option.IsExpanded = args.Key == Key.Right;
                args.Handled = true;
            }
            else if (args.Key is Key.Up or Key.Down && args.OriginalSource is UIElement element)
            {
                element.MoveFocus(new TraversalRequest(args.Key == Key.Down ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous));
                args.Handled = true;
            }
        }
    }

    /// <summary>释放控制器时关闭独立弹出窗口。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs args) => ClosePicker();
    /// <summary>离开最后一步不保留浮在其他步骤上方的面板。</summary>
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!IsVisible) ClosePicker(); }
    /// <summary>窗口移动或停用时不保留原屏幕坐标上的弹出层。</summary>
    private void OnOwnerChanged(object? sender, EventArgs args) => ClosePicker();
    /// <summary>缩放后重新打开时重新计算可用高度。</summary>
    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs args) => ClosePicker();
    /// <summary>模型更换时取消旧目录和旧选择草稿。</summary>
    private static void OnModelChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((InstanceTagSelector)target).ClosePicker();
    /// <summary>依赖属性在 InitializeComponent 前也可能更新，因此允许空弹出引用。</summary>
    private void ClosePicker() { if (TagPopup is not null) TagPopup.IsOpen = false; }
}
