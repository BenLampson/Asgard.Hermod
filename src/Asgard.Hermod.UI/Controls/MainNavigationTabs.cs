namespace Asgard.Hermod.UI.Controls;

/// <summary>主窗体六个固定页签，封装选择、键盘、无缝曲面及主题状态，不创建工作连接。</summary>
public sealed class MainNavigationTabs : ListBox
{
    /// <summary>使用独立公共模板，完全移除原生 ListBox 边框和滚动 Chrome。</summary>
    static MainNavigationTabs() => DefaultStyleKeyProperty.OverrideMetadata(typeof(MainNavigationTabs), new FrameworkPropertyMetadata(typeof(MainNavigationTabs)));
    /// <summary>保留 ListBox 的方向键、Home/End 与辅助技术选择语义。</summary>
    public MainNavigationTabs()
    {
        SelectionMode = SelectionMode.Single;
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
    }
}
