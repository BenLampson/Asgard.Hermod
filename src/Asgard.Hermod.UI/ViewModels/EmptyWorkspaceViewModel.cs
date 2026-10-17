namespace Asgard.Hermod.UI.ViewModels;

/// <summary>承载设计中的首页和分组空内容槽。</summary>
public sealed class EmptyWorkspaceViewModel : WorkspacePageViewModel
{
    /// <summary>创建可关闭的导航页。</summary>
    public EmptyWorkspaceViewModel(string id = "home", string header = "", string icon = "house")
        : base(id, header, icon)
    {
    }
}
