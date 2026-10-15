namespace Asgard.Hermod.UI.Controls;

/// <summary>存储首先使用的连接流程正文；公共流程容器不依赖此表单或连接校验。</summary>
public partial class InstanceConnectionWizardContent : UserControl
{
    /// <summary>类型模块的信息正文插槽，空值时使用通用字段编辑器。</summary>
    public static readonly DependencyProperty InformationContentProperty = DependencyProperty.Register(
        nameof(InformationContent), typeof(object), typeof(InstanceConnectionWizardContent));
    /// <summary>类型模块的信息正文不包含新的 ScrollViewer。</summary>
    public object? InformationContent { get => GetValue(InformationContentProperty); set => SetValue(InformationContentProperty, value); }
    /// <summary>加载服务商、声明式字段、连接测试与安全确认内容。</summary>
    public InstanceConnectionWizardContent() => InitializeComponent();

    /// <summary>宿主先关闭确认页中的品牌下拉，再解释窗口级取消。</summary>
    internal bool CloseHostingProviderDropDown() => ConfirmationHostingProviderSelector.CloseDropDown();

    /// <summary>列表和单选卡使用同一个服务商选择状态。</summary>
    private void OnProviderChecked(object sender, RoutedEventArgs args)
    {
        if (sender is SelectionCard { DataContext: InstanceProviderDescriptor provider })
            ProviderList.SetCurrentValue(ListBox.SelectedItemProperty, provider);
    }

    /// <summary>宿主在正文显示后把焦点交给当前步骤；没有输入控件时交给公共操作栏。</summary>
    internal bool FocusCurrentStep(int step)
    {
        if (step == 1 && ProviderList.IsEnabled) return ProviderList.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Control? target = step switch
        {
            2 when InformationContent is null => NameInput,
            2 => InformationContent as Control,
            3 => StartTestButton,
            _ => null,
        };
        return target?.Focus() == true;
    }
}
