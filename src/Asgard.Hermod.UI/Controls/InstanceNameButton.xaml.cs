using System.Windows.Data;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Controls;

/// <summary>工作区标题的统一改名入口，复用外壳元数据命令并保留当前连接。</summary>
public partial class InstanceNameButton : UserControl
{
    /// <summary>只接收安全导航状态，不读取连接参数。</summary>
    public static readonly DependencyProperty InstanceProperty = DependencyProperty.Register(nameof(Instance),
        typeof(InstanceItemViewModel), typeof(InstanceNameButton), new PropertyMetadata(null));
    /// <summary>当前工作区所属实例。</summary>
    public InstanceItemViewModel? Instance { get => (InstanceItemViewModel?)GetValue(InstanceProperty); set => SetValue(InstanceProperty, value); }
    /// <summary>实际外壳承载时连接命令，独立设计预览无需伪造外壳祖先。</summary>
    public InstanceNameButton()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            for (DependencyObject? parent = this; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is AppShellView shell)
                {
                    RenameButton.SetBinding(Button.CommandProperty, new Binding("DataContext.Sidebar.RenameInstanceCommand") { Source = shell });
                    break;
                }
        };
    }
}
