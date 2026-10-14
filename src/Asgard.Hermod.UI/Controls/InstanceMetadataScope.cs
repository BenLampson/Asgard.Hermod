namespace Asgard.Hermod.UI.Controls;

/// <summary>跨独立窗口显式传递元数据操作，不依赖主窗体视图祖先。</summary>
public static class InstanceMetadataScope
{
    /// <summary>最近宿主提供的改名命令；参数始终为安全实例快照。</summary>
    public static readonly DependencyProperty RenameCommandProperty = DependencyProperty.RegisterAttached("RenameCommand", typeof(ICommand),
        typeof(InstanceMetadataScope), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    /// <summary>读取当前宿主的元数据命令。</summary>
    public static ICommand? GetRenameCommand(DependencyObject target) => (ICommand?)target.GetValue(RenameCommandProperty);
    /// <summary>宿主复用目录命令，保存后统一传播标题与列表更新。</summary>
    public static void SetRenameCommand(DependencyObject target, ICommand? command) => target.SetValue(RenameCommandProperty, command);
}
