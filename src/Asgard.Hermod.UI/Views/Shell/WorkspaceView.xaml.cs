namespace Asgard.Hermod.UI.Views.Shell;

/// <summary>
/// 工作区组合文档页签和保持页面实例的内容宿主。
/// </summary>
public partial class WorkspaceView : UserControl, IDisposable
{
    /// <summary>
    /// 创建工作区。
    /// </summary>
    public WorkspaceView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 释放内容视图，不接管对应 ViewModel 的会话所有权。
    /// </summary>
    public void Dispose()
    {
        ContentHost.Dispose();
        GC.SuppressFinalize(this);
    }
}
