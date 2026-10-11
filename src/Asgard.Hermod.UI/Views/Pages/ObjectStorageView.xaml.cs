namespace Asgard.Hermod.UI.Views.Pages;

/// <summary>组合对象浏览器，页签首次激活时懒加载，关闭由工作区生命周期管理。</summary>
public partial class ObjectStorageView : UserControl
{
    /// <summary>加载真实数据，重复切回页签复用已经初始化的状态。</summary>
    public ObjectStorageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    /// <summary>初始化失败留在页面中，并可通过重试恢复。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (DataContext is ObjectStorageWorkspaceViewModel model && !model.IsClosed)
            await model.InitializeAsync();
    }
}
