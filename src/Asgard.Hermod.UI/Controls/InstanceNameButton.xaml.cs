namespace Asgard.Hermod.UI.Controls;

/// <summary>工作区标题的统一改名入口，复用外壳元数据命令并保留当前连接。</summary>
public partial class InstanceNameButton : UserControl
{
    /// <summary>只接收安全导航状态，不读取连接参数。</summary>
    public static readonly DependencyProperty InstanceProperty = DependencyProperty.Register(nameof(Instance),
        typeof(InstanceItemViewModel), typeof(InstanceNameButton), new PropertyMetadata(null));
    /// <summary>当前工作区所属实例。</summary>
    public InstanceItemViewModel? Instance { get => (InstanceItemViewModel?)GetValue(InstanceProperty); set => SetValue(InstanceProperty, value); }
    /// <summary>从宿主继承元数据命令，嵌入页和独立窗口使用相同保存链路。</summary>
    public InstanceNameButton() => InitializeComponent();
}
