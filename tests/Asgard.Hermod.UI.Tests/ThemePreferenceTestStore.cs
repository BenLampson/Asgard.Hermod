namespace Asgard.Hermod.UI.Tests;

/// <summary>控制主题保存失败，验证命令反馈及资源回滚而不访问用户数据库。</summary>
internal sealed class ThemePreferenceTestStore : IThemePreferenceStore
{
    /// <summary>模拟设备上已有的选择。</summary>
    internal UiTheme? Saved { get; private set; }

    /// <summary>模拟数据库不可写，异常包含不得进入界面的私有信息。</summary>
    internal bool FailSave { get; set; }

    /// <inheritdoc />
    public UiTheme? Load() => Saved;

    /// <inheritdoc />
    public void Save(UiTheme theme)
    {
        if (FailSave) throw new IOException("private-database-path; credential=private-value");
        Saved = theme;
    }
}
