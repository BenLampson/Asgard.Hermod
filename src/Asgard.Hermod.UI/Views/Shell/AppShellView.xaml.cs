namespace Asgard.Hermod.UI.Views.Shell;

/// <summary>主窗体只承载固定导航的实例工作台；独立模块页面由工作窗口呈现。</summary>
public partial class AppShellView : UserControl, IDisposable
{
    /// <summary>组合工作台，不再创建旧左侧实例侧栏或旧品牌首页。</summary>
    public AppShellView() => InitializeComponent();
    /// <summary>关闭未提交的标签筛选草稿。</summary>
    public void Dispose() { MainContent.CloseTagPicker(); GC.SuppressFinalize(this); }
}
