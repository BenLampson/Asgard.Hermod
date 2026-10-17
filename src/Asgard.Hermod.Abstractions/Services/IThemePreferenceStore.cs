namespace Asgard.Hermod.Abstractions.Services;

/// <summary>保存当前设备上的明暗主题选择，未设置时沿用应用默认主题。</summary>
public interface IThemePreferenceStore
{
    /// <summary>读取保存的主题；未设置或无法识别时返回空值。</summary>
    UiTheme? Load();

    /// <summary>立即原子保存主题，供下次启动恢复。</summary>
    void Save(UiTheme theme);
}
