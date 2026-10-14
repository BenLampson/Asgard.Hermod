namespace Asgard.Hermod.UI.Windowing;

/// <summary>模块弹窗跟随实际活动工作窗口，全应用预检可指定当前检查窗口。</summary>
public static class DialogOwner
{
    /// <summary>预检时由宿主设置，结束后立即清空。</summary>
    public static Window? Override { get; set; }
    /// <summary>保留当前模态链，避免工作窗口弹窗错误附着到隐藏主窗体。</summary>
    public static Window? Current => Override ?? Application.Current?.Windows.Cast<Window>().FirstOrDefault(window => window.IsActive);
}
