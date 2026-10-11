namespace Asgard.Hermod.UI.Themes;

/// <summary>
/// 替换应用中的颜色字典，保留字体、样式和现有视图。
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private ResourceDictionary _themeDictionary;

    /// <summary>
    /// 使用应用已经装配的主题；没有颜色字典时初始化为明亮主题。
    /// </summary>
    public ThemeService(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _resources = resources;
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
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "未知的界面主题。");
        }

        if (theme == Theme)
        {
            return;
        }

        // 替换而非追加，避免切换次数增加后产生资源覆盖和字典泄漏。
        var dictionary = CreateDictionary(theme);
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
