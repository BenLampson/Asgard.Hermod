using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.ObjectStorage.UI.Views.Pages;

/// <summary>只读对象预览与元数据，更多操作统一使用页面命令。</summary>
public partial class ObjectStorageDetailsView : UserControl
{
    /// <summary>创建固定宽度的对象详情面板。</summary>
    public ObjectStorageDetailsView() => InitializeComponent();
    /// <summary>当前对象菜单不缓存对象键，选择变化后直接使用当前命令目标。</summary>
    private void OnMoreClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || DataContext is not ObjectStorageWorkspaceViewModel { SelectedEntry: { } row } model) return;
        var menu = ObjectStorageMenuFactory.Create(model, row, button, includeDownload: false);
        menu.IsOpen = true;
        args.Handled = true;
    }
}
