using System.Collections;
using System.Collections.Specialized;
using System.Globalization;

namespace Asgard.Hermod.UI.Controls;

/// <summary>封装可搜索的单选弹层，筛选与键盘候选独立于已经确认的业务值。</summary>
public partial class SearchableSelect : UserControl
{
    /// <summary>业务提供的原始选项，可使用可观察集合实时更新。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SearchableSelect), new PropertyMetadata(null, OnItemsSourceChanged));
    /// <summary>只有用户确认时才改变选择，外部更新不会发出确认事件。</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SearchableSelect), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));
    /// <summary>用于选项名称及筛选的文本属性路径。</summary>
    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(SearchableSelect), new PropertyMetadata("", OnDisplayPathChanged));
    /// <summary>选择框前缀图标，可以省略。</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SearchableSelect), new PropertyMetadata(""));
    /// <summary>当前弹层内的筛选词，不参与业务选择绑定。</summary>
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(SearchableSelect), new PropertyMetadata("", OnFilterChanged));
    /// <summary>控制弹层开关，点击外部和 Escape 都会关闭。</summary>
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(SearchableSelect), new PropertyMetadata(false));
    /// <summary>当前已确认选项的可见名称。</summary>
    public static readonly DependencyProperty SelectedLabelProperty = DependencyProperty.Register(nameof(SelectedLabel), typeof(string), typeof(SearchableSelect), new PropertyMetadata(Text.Get("SelectAnOption")));
    /// <summary>筛选结果是否非空。</summary>
    public static readonly DependencyProperty HasOptionsProperty = DependencyProperty.Register(nameof(HasOptions), typeof(bool), typeof(SearchableSelect), new PropertyMetadata(false));
    /// <summary>弹层底部显示当前筛选数量。</summary>
    public static readonly DependencyProperty OptionCountTextProperty = DependencyProperty.Register(nameof(OptionCountText), typeof(string), typeof(SearchableSelect), new PropertyMetadata(""));
    /// <summary>明确选择现有值也会通知宿主，适用于回到当前容器根目录。</summary>
    public static readonly RoutedEvent SelectionCommittedEvent = EventManager.RegisterRoutedEvent(nameof(SelectionCommitted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SearchableSelect));

    /// <summary>原始选项集合。</summary>
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>当前已经确认的业务对象。</summary>
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    /// <summary>选项文本属性路径。</summary>
    public string DisplayMemberPath { get => (string)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
    /// <summary>选择框的可选前缀图标。</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    /// <summary>弹层局部筛选词。</summary>
    public string SearchText { get => (string)GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    /// <summary>弹层是否展开。</summary>
    public bool IsDropDownOpen { get => (bool)GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }
    /// <summary>触发按钮显示的名称。</summary>
    public string SelectedLabel { get => (string)GetValue(SelectedLabelProperty); private set => SetValue(SelectedLabelProperty, value); }
    /// <summary>是否有可见候选项。</summary>
    public bool HasOptions { get => (bool)GetValue(HasOptionsProperty); private set => SetValue(HasOptionsProperty, value); }
    /// <summary>筛选结果数量说明。</summary>
    public string OptionCountText { get => (string)GetValue(OptionCountTextProperty); private set => SetValue(OptionCountTextProperty, value); }
    /// <summary>与业务原始集合隔离的可见候选项。</summary>
    public ObservableCollection<SelectOption> Options { get; } = [];
    /// <summary>由点击或 Enter 发出的明确选择事件。</summary>
    public event RoutedEventHandler SelectionCommitted { add => AddHandler(SelectionCommittedEvent, value); remove => RemoveHandler(SelectionCommittedEvent, value); }

    /// <summary>装配共享选择器，卸载时关闭独立弹层。</summary>
    public SearchableSelect()
    {
        InitializeComponent();
        Unloaded += (_, _) => SetCurrentValue(IsDropDownOpenProperty, false);
        IsEnabledChanged += (_, _) => { if (!IsEnabled) SetCurrentValue(IsDropDownOpenProperty, false); };
    }

    /// <summary>弱订阅原始集合，替换集合时解除旧订阅。</summary>
    private static void OnItemsSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var select = (SearchableSelect)owner;
        if (args.OldValue is INotifyCollectionChanged previous) CollectionChangedEventManager.RemoveHandler(previous, select.OnCollectionChanged);
        if (args.NewValue is INotifyCollectionChanged current) CollectionChangedEventManager.AddHandler(current, select.OnCollectionChanged);
        select.RebuildOptions();
    }

    /// <summary>外部选择更新只改变显示，不模拟用户操作。</summary>
    private static void OnSelectionChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((SearchableSelect)owner).RebuildOptions(resetCandidate: true);
    /// <summary>文本路径更新后重新读取显示名称。</summary>
    private static void OnDisplayPathChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((SearchableSelect)owner).RebuildOptions();
    /// <summary>输入筛选时保留原始业务选择及数据源。</summary>
    private static void OnFilterChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((SearchableSelect)owner).RebuildOptions();
    /// <summary>容器读取完成或连接编辑后，同步候选集合。</summary>
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => RebuildOptions();

    /// <summary>筛选只操作轻量候选，选中名称始终来自已经确认的业务对象。</summary>
    private void RebuildOptions(bool resetCandidate = false)
    {
        SelectedLabel = SelectedItem is null ? Text.Get("SelectAnOption") : Label(SelectedItem);
        var all = ItemsSource?.Cast<object>().ToArray() ?? [];
        var candidate = !resetCandidate && OptionsList?.SelectedItem is SelectOption option ? option.Value : SelectedItem;
        Options.Clear();
        foreach (var value in all)
        {
            string label = Label(value);
            if (label.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)) Options.Add(new(value, label, Equals(value, SelectedItem)));
        }
        HasOptions = Options.Count > 0;
        OptionCountText = Text.Format("TypeToFilter", Options.Count, all.Length);
        if (OptionsList is not null) OptionsList.SelectedItem = Options.FirstOrDefault(item => Equals(item.Value, candidate)) ?? Options.FirstOrDefault();
    }

    /// <summary>支持普通对象及嵌套文本属性，未指定时使用对象自身名称。</summary>
    private string Label(object value)
    {
        object? text = value;
        foreach (string property in DisplayMemberPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            text = text is null ? null : TypeDescriptor.GetProperties(text)[property]?.GetValue(text);
        return Convert.ToString(text, CultureInfo.CurrentCulture) ?? "";
    }

    /// <summary>鼠标点击触发按钮切换弹层。</summary>
    private void OnTriggerClick(object sender, RoutedEventArgs args) => SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
    /// <summary>方向键或 F4 可以从触发按钮打开选择。</summary>
    private void OnTriggerKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Down or Key.F4)) return;
        SetCurrentValue(IsDropDownOpenProperty, true); args.Handled = true;
    }

    /// <summary>打开时重置局部筛选并将焦点交给搜索输入。</summary>
    private void OnPopupOpened(object? sender, EventArgs args)
    {
        SetCurrentValue(SearchTextProperty, ""); RebuildOptions(resetCandidate: true);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsDropDownOpen) return;
            if (OptionsList.SelectedItem is not null) OptionsList.ScrollIntoView(OptionsList.SelectedItem);
            FilterInput.Focus(); FilterInput.SelectAll();
        }));
    }

    /// <summary>关闭时取消局部筛选，业务值始终保留。</summary>
    private void OnPopupClosed(object? sender, EventArgs args) => SetCurrentValue(SearchTextProperty, "");
    /// <summary>Escape 关闭弹层并回到原触发控件。</summary>
    private void OnPopupKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        SetCurrentValue(IsDropDownOpenProperty, false); TriggerButton.Focus(); args.Handled = true;
    }

    /// <summary>搜索内 Enter 确认可见候选，向下键进入候选列表。</summary>
    private void OnFilterKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && OptionsList.SelectedItem is SelectOption option) { Commit(option); args.Handled = true; }
        else if (args.Key == Key.Down && OptionsList.SelectedItem is not null)
        {
            var candidate = OptionsList.SelectedItem;
            OptionsList.ScrollIntoView(candidate);
            // 虚拟化列表先完成滚动和容器生成，再将焦点交给仍然有效的候选。
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!IsDropDownOpen || !ReferenceEquals(OptionsList.SelectedItem, candidate)) return;
                OptionsList.UpdateLayout();
                if (OptionsList.ItemContainerGenerator.ContainerFromItem(candidate) is ListBoxItem item) item.Focus();
            }));
            args.Handled = true;
        }
    }

    /// <summary>候选列表 Enter 确认，方向键只移动候选。</summary>
    private void OnOptionsKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && OptionsList.SelectedItem is SelectOption option) { Commit(option); args.Handled = true; }
    }

    /// <summary>只在实际选项上点击时确认，滚动条操作不会改变业务值。</summary>
    private void OnOptionClick(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(OptionsList, source) is ListBoxItem { DataContext: SelectOption option })
        { Commit(option); args.Handled = true; }
    }

    /// <summary>清空没有匹配项的筛选，焦点留在输入框。</summary>
    private void OnClearFilter(object sender, RoutedEventArgs args) { SetCurrentValue(SearchTextProperty, ""); FilterInput.Focus(); }
    /// <summary>提交一次选择，即使值相同也允许宿主响应明确的根目录操作。</summary>
    private void Commit(SelectOption option)
    {
        SetCurrentValue(SelectedItemProperty, option.Value);
        SetCurrentValue(IsDropDownOpenProperty, false);
        TriggerButton.Focus();
        RaiseEvent(new RoutedEventArgs(SelectionCommittedEvent, this));
    }
}
