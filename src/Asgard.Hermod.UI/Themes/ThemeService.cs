namespace Asgard.Hermod.UI.Themes;

/// <summary>
/// 替换应用中的颜色字典，保留字体、样式和现有视图。
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private readonly IThemePreferenceStore? _preferences;
    private ResourceDictionary _themeDictionary;

    /// <summary>
    /// 在创建视图前恢复保存的主题；独立预览未装配存储时沿用现有颜色字典。
    /// </summary>
    public ThemeService(ResourceDictionary resources, IThemePreferenceStore? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _resources = resources;
        _preferences = preferences;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _dispatcher.VerifyAccess();

        _themeDictionary = resources.MergedDictionaries.FirstOrDefault(IsThemeDictionary)
            ?? CreateDictionary(UiTheme.Light);
        Theme = _themeDictionary.Source.OriginalString.EndsWith("Theme.Dark.xaml", StringComparison.Ordinal)
            ? UiTheme.Dark
            : UiTheme.Light;

        // 主题放在基础样式之前，确保动态颜色键始终可解析。
        if (!resources.MergedDictionaries.Contains(_themeDictionary))
        {
            resources.MergedDictionaries.Insert(0, _themeDictionary);
        }

        // 启动恢复只读取偏好，不能把默认明亮主题写回覆盖用户选择。
        if (_preferences?.Load() is { } savedTheme && savedTheme != Theme)
            ApplyTheme(savedTheme, CreateDictionary(savedTheme));
    }

    /// <inheritdoc />
    public UiTheme Theme { get; private set; }

    /// <inheritdoc />
    public event EventHandler? ThemeChanged;

    /// <inheritdoc />
    public void SetTheme(UiTheme theme)
    {
        _dispatcher.VerifyAccess();
        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, Text.Get("UnknownInterfaceTheme"));
        }

        if (theme == Theme)
        {
            return;
        }

        // 替换而非追加，避免切换次数增加后产生资源覆盖和字典泄漏。
        var dictionary = CreateDictionary(theme);
        // 保存失败时保持原主题，由命令边界显示安全文案，避免产生虚假的已保存状态。
        try { _preferences?.Save(theme); }
        catch (Exception) { throw Text.Exception("CannotSaveThemePreference"); }
        ApplyTheme(theme, dictionary);
    }

    /// <summary>首次恢复和运行时切换共用资源替换逻辑，恢复时不回写偏好。</summary>
    private void ApplyTheme(UiTheme theme, ResourceDictionary dictionary)
    {
        var index = _resources.MergedDictionaries.IndexOf(_themeDictionary);
        if (index >= 0)
        {
            _resources.MergedDictionaries[index] = dictionary;
        }
        else
        {
            _resources.MergedDictionaries.Insert(0, dictionary);
        }

        _themeDictionary = dictionary;
        Theme = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>识别本项目的主题字典，不匹配字体或基础样式。</summary>
    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null && (source.EndsWith("Resources/Theme.Light.xaml", StringComparison.Ordinal)
            || source.EndsWith("Resources/Theme.Dark.xaml", StringComparison.Ordinal));
    }

    /// <summary>通过程序集资源地址加载颜色，支持主应用和独立视图预览。</summary>
    private static ResourceDictionary CreateDictionary(UiTheme theme)
    {
        return new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Asgard.Hermod.UI;component/Resources/Theme.{theme}.xaml",
                UriKind.Absolute)
        };
    }
}
