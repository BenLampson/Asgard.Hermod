namespace Asgard.Hermod.UI.Controls;

/// <summary>只读的单行实例身份；名称、标签与连接配置统一在主窗体编辑。</summary>
public partial class WorkspaceIdentity : UserControl
{
    /// <summary>实例快照，不包含配置编辑命令。</summary>
    public static readonly DependencyProperty InstanceProperty = DependencyProperty.Register(nameof(Instance), typeof(InstanceItemViewModel), typeof(WorkspaceIdentity));
    /// <summary>安全地址，不包含密码与连接字符串。</summary>
    public static readonly DependencyProperty EndpointProperty = DependencyProperty.Register(nameof(Endpoint), typeof(string), typeof(WorkspaceIdentity));
    /// <summary>共享品牌或协议图标。</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(WorkspaceIdentity));
    /// <summary>实例。</summary>
    public InstanceItemViewModel? Instance { get => (InstanceItemViewModel?)GetValue(InstanceProperty); set => SetValue(InstanceProperty, value); }
    /// <summary>安全端点。</summary>
    public string? Endpoint { get => (string?)GetValue(EndpointProperty); set => SetValue(EndpointProperty, value); }
    /// <summary>图标标识。</summary>
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    /// <summary>创建共享标题身份视图。</summary>
    public WorkspaceIdentity() => InitializeComponent();
}
