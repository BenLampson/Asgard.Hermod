namespace Asgard.Hermod.UI.Themes;

/// <summary>
/// 外壳使用的主题切换契约，隔离资源字典的具体装配方式。
/// </summary>
public interface IThemeService
{
    /// <summary>当前已应用的主题。</summary>
    UiTheme Theme { get; }

    /// <summary>颜色字典完成替换后通知绑定状态。</summary>
    event EventHandler? ThemeChanged;

    /// <summary>在界面线程应用指定主题，重复设置不重复装配。</summary>
    void SetTheme(UiTheme theme);
}
