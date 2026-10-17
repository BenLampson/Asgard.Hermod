namespace Asgard.Hermod.UI.Controls;

/// <summary>可独立最大化与跨屏移动的助手；不设置 Owner，隐藏不取消业务请求。</summary>
public sealed class AgentChatWindow : HermodWindow
{
    private bool _closing;
    /// <summary>只在显式打开入口构造原生窗口；视觉验证直接渲染 AgentSourceView。</summary>
    public AgentChatWindow(AgentWindowRequest request)
    {
        Title = Text.Get("ChatWindowTitle"); Width = 1120; Height = 1000; MinWidth = 640; MinHeight = 600; CaptionHeight = 44;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = true;
        CaptionContent = new AgentWindowCaption();
        Content = new AgentSourceView(request);
        Closing += (_, args) => { if (!_closing) { args.Cancel = true; Hide(); } };
    }
    /// <summary>仅应用退出释放窗口，普通关闭保留位置、草稿与任务。</summary>
    public void ClosePermanently() { _closing = true; Close(); }
    /// <summary>初始尺寸按当前屏幕工作区限制，跨屏后沿用原生 DPI 布局。</summary>
    protected override void OnSourceInitialized(EventArgs args) { base.OnSourceInitialized(args); Windowing.WindowStartupPlacement.Apply(this); }
}
