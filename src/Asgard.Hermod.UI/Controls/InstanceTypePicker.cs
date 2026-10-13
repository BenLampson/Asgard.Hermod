namespace Asgard.Hermod.UI.Controls;

/// <summary>独立的两列类型入口，只展示注册表中的类型，不依赖任何具体连接流程。</summary>
public class InstanceTypePicker : ListBox
{
    /// <summary>使用共享模板和普通列表选择绑定，键盘与鼠标使用相同的选中路径。</summary>
    public InstanceTypePicker() => AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
        new RoutedEventHandler(OnCardChecked));

    /// <summary>自动加载两列卡片模板；列表内不创建第二个滚动区域。</summary>
    static InstanceTypePicker() => DefaultStyleKeyProperty.OverrideMetadata(typeof(InstanceTypePicker),
        new FrameworkPropertyMetadata(typeof(InstanceTypePicker)));

    /// <summary>单选卡的选择同步给列表，再由调用者决定如何打开对应流程。</summary>
    private void OnCardChecked(object sender, RoutedEventArgs args)
    {
        if (args.OriginalSource is SelectionCard { DataContext: InstanceTypeDescriptor type })
            SetCurrentValue(SelectedItemProperty, type);
    }
}
