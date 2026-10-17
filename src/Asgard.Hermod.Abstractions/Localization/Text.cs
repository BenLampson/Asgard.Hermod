using System.Globalization;
using System.Resources;

namespace Asgard.Hermod.Abstractions.Localization;

/// <summary>所有模块共享的文案资源入口；不翻译用户数据、协议值或终端输出。</summary>
public static class Text
{
    private static CultureInfo? _applicationCulture;
    private static readonly ResourceManager Resources = new(
        "Asgard.Hermod.Abstractions.Localization.Strings", typeof(Text).Assembly);

    /// <summary>新增语言时添加对应卫星资源及这一项，优先匹配具体语言。</summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } = Array.AsReadOnly<LanguageOption>(
    [
        new("en", "English", ["en"]),
        new("zh-Hans", "简体中文", ["zh"]),
    ]);

    /// <summary>界面文化只控制文案；数字、日期及协议序列化继续使用原有文化约定。</summary>
    public static CultureInfo Culture => Volatile.Read(ref _applicationCulture) ?? ResolveCulture(null, CultureInfo.CurrentUICulture);

    /// <summary>宿主在读取保存偏好后固定应用资源语言，避免 Dispatcher 和异步上下文恢复成 Windows 语言。
    /// 传入 null 恢复按当前上下文解析，供独立测试使用；区域格式仍不改变。</summary>
    public static void SetApplicationCulture(CultureInfo? culture) => Volatile.Write(ref _applicationCulture,
        culture is null ? null : ResolveCulture(null, culture));

    /// <summary>手动选择优先，跟随当前 Windows 用户显示语言，未支持时回退英语。</summary>
    public static CultureInfo ResolveCulture(string? preference, CultureInfo systemCulture)
    {
        ArgumentNullException.ThrowIfNull(systemCulture);
        var explicitLanguage = Languages.FirstOrDefault(language =>
            string.Equals(language.Id, preference, StringComparison.OrdinalIgnoreCase));
        var detectedLanguage = Languages.FirstOrDefault(language => language.SystemLanguagePrefixes.Any(prefix =>
            systemCulture.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            systemCulture.Name.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)));
        return CultureInfo.GetCultureInfo((string.IsNullOrWhiteSpace(preference)
            ? detectedLanguage ?? Languages[0] : explicitLanguage ?? Languages[0]).Id);
    }

    /// <summary>在创建视图与连接器之前设置主线程及后续后台线程的资源语言。</summary>
    public static void Initialize(string? preference, CultureInfo systemCulture)
    {
        var culture = ResolveCulture(preference, systemCulture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>资源缺失时由 ResourceManager 回退英语；未知键立即暴露维护错误。</summary>
    public static string Get(string key) => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException($"Missing Hermod resource: {key}");

    /// <summary>使用整句模板保留参数位置、格式与语序；禁止拼接可翻译句子。</summary>
    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>业务异常携带稳定资源键与参数，保留原有异常类型以兼容调用方。</summary>
    public static InvalidOperationException Exception(string key, params object?[] arguments)
    {
        var exception = new InvalidOperationException(Format(key, arguments));
        exception.Data["Hermod.ResourceKey"] = key;
        exception.Data["Hermod.ResourceArguments"] = arguments;
        return exception;
    }

    /// <summary>仅呈现带资源键的业务错误；未知系统、SDK 或数据库异常使用安全提示。</summary>
    public static string Error(System.Exception exception, string fallbackKey = "TheOperationFailedTryAgain")
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is ILocalizedError localized)
            return Format(localized.ResourceKey, localized.ResourceArguments);
        if (exception.Data["Hermod.ResourceKey"] is string key &&
            exception.Data["Hermod.ResourceArguments"] is object?[] arguments)
            return Format(key, arguments);
        return Get(fallbackKey);
    }
}
