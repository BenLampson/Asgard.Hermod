namespace Asgard.Hermod.UI.Localization;

/// <summary>仅翻译系统保留的环境标签显示名称，原始路径始终用于存储和筛选。</summary>
public static class TagDisplayText
{
    /// <summary>保留自定义名称，只解析系统固定分组和环境名称。</summary>
    public static string Name(string path, string original) => path.ToUpperInvariant() switch
    {
        "ENVIRONMENT" => Text.Get("Environment"),
        "ENVIRONMENT/PRODUCTION" => Text.Get("Production"),
        "ENVIRONMENT/STAGING" => Text.Get("Staging"),
        "ENVIRONMENT/DEVELOPMENT" => Text.Get("Development"),
        _ => original,
    };

    /// <summary>摘要保留最后两层，系统标签显示翻译，用户层级名称原样显示。</summary>
    public static string Summary(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" / ", parts.Select((part, index) => Name(string.Join('/', parts.Take(index + 1)), part)).TakeLast(2));
    }
}
