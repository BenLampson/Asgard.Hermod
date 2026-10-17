namespace Asgard.Hermod.UI.Controls;

/// <summary>实例元数据窗口和连接确认页共用托管品牌选择器。</summary>
public partial class InstanceHostingProviderSelector : UserControl
{
    /// <summary>只回传稳定品牌标识，空字符串表示自部署。</summary>
    public static readonly DependencyProperty ProviderIdProperty = DependencyProperty.Register(nameof(ProviderId), typeof(string),
        typeof(InstanceHostingProviderSelector), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>控件在应用语言确定后构造，不缓存进程启动前的翻译。</summary>
    public InstanceHostingProviderSelector() => InitializeComponent();

    /// <summary>品牌选项来自共享注册目录。</summary>
    public IReadOnlyList<InstanceHostingProvider> Providers { get; } = InstanceHostingProviders.All;

    /// <summary>当前选择不改变连接器 ProviderId。</summary>
    public string ProviderId { get => (string)GetValue(ProviderIdProperty); set => SetValue(ProviderIdProperty, value); }

    /// <summary>Escape 优先收起品牌下拉，保留窗口中的其他未保存信息。</summary>
    public bool CloseDropDown()
    {
        if (!ProviderInput.IsDropDownOpen) return false;
        ProviderInput.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
        ProviderInput.Focus();
        return true;
    }
}
