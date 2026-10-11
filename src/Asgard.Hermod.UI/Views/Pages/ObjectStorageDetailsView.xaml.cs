using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Pages;

/// <summary>只读对象预览与元数据，更多操作统一使用页面命令。</summary>
public partial class ObjectStorageDetailsView : UserControl
{
    /// <summary>创建固定宽度的对象详情面板。</summary>
    public ObjectStorageDetailsView() => InitializeComponent();
    /// <summary>当前对象菜单不缓存对象键，选择变化后直接使用当前命令目标。</summary>
    private void OnMoreClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || DataContext is not ObjectStorageWorkspaceViewModel { SelectedEntry: { } row } model) return;
        var menu = new ContextMenu { PlacementTarget = button };
        menu.Items.Add(new MenuItem { Header = "复制 Object Key", Command = model.CopyKeyCommand, CommandParameter = row, Icon = new IconView { Kind = "copy", Width = 15, Height = 15 } });
        menu.Items.Add(new MenuItem { Header = "生成临时链接…", Command = model.TemporaryLinkCommand, CommandParameter = row, Icon = new IconView { Kind = "link", Width = 15, Height = 15 } });
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "删除对象…", Command = model.DeleteCommand, CommandParameter = row, Icon = new IconView { Kind = "trash-2", Width = 15, Height = 15 } };
        delete.SetResourceReference(ForegroundProperty, "DangerBrush");
        menu.Items.Add(delete); menu.IsOpen = true;
    }
}
