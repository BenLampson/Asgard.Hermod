namespace Asgard.Hermod.UI.Controls;

/// <summary>与 Pencil 一致的标签树选择面板；由宿主控制弹出层与最终筛选。</summary>
public partial class TagTreePicker : UserControl
{
    // 固定区域和最小树区共占二百八十一像素，摘要只使用剩余的空间。
    private static readonly DependencyPropertyKey MaximumChipHeightPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MaximumChipHeight), typeof(double), typeof(TagTreePicker), new PropertyMetadata(96d));

    /// <summary>标签摘要的可用高度由当前面板尺寸计算，防止挤出底部操作。</summary>
    public static readonly DependencyProperty MaximumChipHeightProperty = MaximumChipHeightPropertyKey.DependencyProperty;

    /// <summary>允许宿主在每次打开时注入独立草稿，关闭后可以清空。</summary>
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(TagTreePickerViewModel), typeof(TagTreePicker), new PropertyMetadata(null));

    /// <summary>初始化树、搜索和提交入口。</summary>
    public TagTreePicker()
    {
        InitializeComponent();
        SizeChanged += OnPickerSizeChanged;
    }

    /// <summary>当前摘要滚动区上限，至少为零且最多为九十六像素。</summary>
    public double MaximumChipHeight => (double)GetValue(MaximumChipHeightProperty);

    /// <summary>当前选择草稿；null 用于弹出层关闭后的引用释放。</summary>
    public TagTreePickerViewModel? Model
    {
        get => (TagTreePickerViewModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>弹出层挂载后将键盘焦点送入搜索框。</summary>
    public void FocusSearch()
    {
        Dispatcher.VerifyAccess();
        SearchInput.Focus();
        SearchInput.SelectAll();
    }

    /// <summary>缩小面板时优先压缩摘要，保留标题、导航、树和应用按钮。</summary>
    private void OnPickerSizeChanged(object sender, SizeChangedEventArgs e) =>
        SetValue(MaximumChipHeightPropertyKey, Math.Clamp(ActualHeight - 281, 0, 96));

    /// <summary>Escape 先收起匹配模式菜单，再取消整个草稿；方向键展开当前行。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (MatchModeSelect.IsDropDownOpen)
                return;
            Model?.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Left or Key.Right)
            || e.OriginalSource is not FrameworkElement { DataContext: TagTreeNodeViewModel node }
            || !node.HasChildren)
            return;
        bool expand = e.Key == Key.Right;
        if (node.IsEffectivelyExpanded != expand)
            node.ToggleExpansionCommand.Execute(null);
        e.Handled = true;
    }
}
